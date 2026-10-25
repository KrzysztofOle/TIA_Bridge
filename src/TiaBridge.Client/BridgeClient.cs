using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using TiaBridge.Core.Transport.V1;

namespace TiaBridge.Client;

public sealed record ClientResponse(int StatusCode, byte[] Json);

/// <summary>No queue, storage, worker, retry or cancellation of accepted operations.</summary>
public sealed class BridgeClient : IDisposable
{
    private readonly HttpClient http;
    [SupportedOSPlatform("windows")]
    public BridgeClient(string discoveryPath) : this(DiscoveryFile.Read(discoveryPath)) { }
    public BridgeClient(HostDiscovery discovery)
    {
        if (!Uri.TryCreate(discovery.BaseUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" || endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.UserInfo.Length != 0 || endpoint.Fragment.Length != 0) throw new ArgumentException("Approved IPv4 loopback endpoint required.");
        http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(2), MaxConnectionsPerServer = 8 })
        { BaseAddress = new Uri(discovery.BaseUrl), Timeout = TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = ContractJson.MaxReportBytes };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", discovery.Token);
    }
    public async Task<ClientResponse> SendAsync(HttpMethod method, string relative, byte[]? body = null, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5)); ct = deadline.Token;
        if (!relative.StartsWith("/v1/", StringComparison.Ordinal) || relative.Contains("..", StringComparison.Ordinal) || relative.Contains('\\')) throw new ArgumentException("Invalid API path.");
        using var request = new HttpRequestMessage(method, relative);
        if (body is not null)
        {
            if (body.Length > ContractJson.MaxRequestBytes) throw new ArgumentException("Request exceeds limit.");
            request.Content = new ByteArrayContent(body); request.Content.Headers.ContentType = new("application/json");
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.Content.Headers.ContentLength > ContractJson.MaxReportBytes) throw new IOException("Response exceeds contract limit.");
        using var content = await response.Content.ReadAsStreamAsync(ct); using var output = new MemoryStream(); var buffer = new byte[16384];
        while (true)
        {
            int read = await content.ReadAsync(buffer, ct); if (read == 0) break;
            if (output.Length + read > ContractJson.MaxReportBytes) throw new IOException("Response exceeds contract limit.");
            output.Write(buffer, 0, read);
        }
        byte[] json = output.ToArray();
        try
        {
            if ((int)response.StatusCode >= 400) _ = ContractJson.ReadError(json);
            else if (response.StatusCode == HttpStatusCode.Accepted && relative == "/v1/operations") _ = ContractJson.ReadAcceptance(json);
            else _ = ContractJson.ReadReport(json);
        }
        catch (ContractValidationException) { throw new IOException("Host response violates the contract."); }
        return new((int)response.StatusCode, json);
    }
    public void Dispose() => http.Dispose();
}
