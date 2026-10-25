using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TiaBridge.Client;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;

if (!OperatingSystem.IsWindows()) return Fail(ErrorCode.UNSUPPORTED, 2);
Console.OutputEncoding = new UTF8Encoding(false);
using var interrupt = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; interrupt.Cancel(); };
try
{
    var positional = new List<string>(); var options = new Dictionary<string, string?>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) { positional.Add(args[i]); continue; }
        string option = args[i];
        if (!new[] { "--json", "--wait", "--max-wait", "--discovery", "--request", "--after-sequence", "--limit", "--reason" }.Contains(option) || options.ContainsKey(option)) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
        if (option is "--json" or "--wait") options.Add(option, null);
        else { if (++i == args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) return Fail(ErrorCode.INVALID_ARGUMENT, 2); options.Add(option, args[i]); }
    }
    bool wait = options.ContainsKey("--wait") || positional.Count > 1 && positional[0] == "operation" && positional[1] == "wait";
    if (positional.Count == 0 || wait && !options.ContainsKey("--max-wait") || !wait && options.ContainsKey("--max-wait")) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
    int maxWait = wait ? int.Parse(options["--max-wait"]!, System.Globalization.CultureInfo.InvariantCulture) : 0;
    if (wait && maxWait is < 1 or > 3600) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
    string? command = null; string path; HttpMethod method = HttpMethod.Get; byte[]? body = null;
    var allowed = new HashSet<string>(["--json", "--discovery"]);
    if (positional[0] == "submit" && positional.Count == 2)
    {
        command = "submit"; if (!options.TryGetValue("--request", out var requestFile)) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
        allowed.UnionWith(["--request", "--wait", "--max-wait"]);
        using var file = new FileStream(requestFile!, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > ContractJson.MaxRequestBytes) return Fail(ErrorCode.LIMIT_EXCEEDED, 2);
        body = new byte[checked((int)file.Length)]; file.ReadExactly(body); var request = ContractJson.ReadRequest(body);
        if (OperationCatalog.Name(request.Identity.Method) != positional[1]) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
        method = HttpMethod.Post; path = "/v1/operations";
    }
    else if (positional[0] == "health" && positional.Count == 1) { command = "health"; path = "/v1/diagnostics/health"; }
    else if (positional[0] == "operation" && positional.Count == 3 && new[] { "status", "result", "events", "cancel", "wait" }.Contains(positional[1]))
    {
        command = positional[1]; var id = new OperationId(UuidIdentity.ParseValue(positional[2])); path = "/v1/operations/" + id;
        if (command is "result" or "events" or "cancel") path += "/" + command;
        if (command == "wait") allowed.Add("--max-wait");
        if (command == "events")
        {
            allowed.UnionWith(["--after-sequence", "--limit"]);
            long after = options.TryGetValue("--after-sequence", out var a) ? long.Parse(a!, System.Globalization.CultureInfo.InvariantCulture) : 0;
            int limit = options.TryGetValue("--limit", out var l) ? int.Parse(l!, System.Globalization.CultureInfo.InvariantCulture) : 100;
            if (after < 0 || limit is < 1 or > 1000) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
            path += "?after_sequence=" + after + "&limit=" + limit;
        }
        if (command == "cancel")
        {
            allowed.Add("--reason"); method = HttpMethod.Post;
            if (options.TryGetValue("--reason", out var reason))
            {
                if (reason!.EnumerateRunes().Count() > 512) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
                body = JsonSerializer.SerializeToUtf8Bytes(new { reason });
            }
        }
    }
    else return Fail(ErrorCode.INVALID_ARGUMENT, 2);
    if (options.Keys.Any(k => !allowed.Contains(k))) return Fail(ErrorCode.INVALID_ARGUMENT, 2);
    using var client = new BridgeClient(options.GetValueOrDefault("--discovery") ?? DiscoveryFile.DefaultPath);
    var response = await client.SendAsync(method, path, body, interrupt.Token);
    if (response.StatusCode >= 400) { Print(response.Json); return response.StatusCode is 401 or 403 or 409 or 422 ? 3 : response.StatusCode is 400 or 413 ? 2 : 4; }
    if (!wait) { Print(response.Json); return command == "result" && Failed(ContractJson.ReadReport(response.Json).State) ? 6 : 0; }
    OperationId operation = response.StatusCode == 202 && command == "submit" ? ContractJson.ReadAcceptance(response.Json).OperationId : ContractJson.ReadReport(response.Json).OperationId;
    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(interrupt.Token); bounded.CancelAfter(TimeSpan.FromSeconds(maxWait));
    byte[] latest = response.Json;
    try
    {
        while (true)
        {
            var current = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + operation, ct: bounded.Token);
            if (current.StatusCode >= 400) { Print(current.Json); return 4; }
            latest = current.Json; var report = ContractJson.ReadReport(latest);
            if (OperationLifecycle.IsTerminal(report.State))
            {
                var final = await client.SendAsync(HttpMethod.Get, "/v1/operations/" + operation + "/result", ct: bounded.Token);
                Print(final.Json); return final.StatusCode >= 400 ? 4 : Failed(report.State) ? 6 : 0;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(report.PollAfterMs, 100, 5000)), bounded.Token);
        }
    }
    catch (OperationCanceledException) { Print(latest); return 5; }
}
catch (ContractValidationException) { return Fail(ErrorCode.VALIDATION_FAILED, 2); }
catch (Exception e) when (e is ArgumentException or FormatException or OverflowException or JsonException) { return Fail(ErrorCode.INVALID_ARGUMENT, 2); }
catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
{ return Fail(ErrorCode.WORKER_UNAVAILABLE, 4); }

static bool Failed(OperationState state) => state is OperationState.FAILED or OperationState.RECONCILIATION_REQUIRED;
static void Print(byte[] json) => Console.WriteLine(Encoding.UTF8.GetString(json));
static int Fail(ErrorCode code, int exit)
{ Print(ContractJson.WriteError(new(code, exit == 4 ? ErrorCategory.TRANSPORT : ErrorCategory.VALIDATION, "CLI request could not be completed.", false, OperationOutcome.NOT_STARTED))); return exit; }
