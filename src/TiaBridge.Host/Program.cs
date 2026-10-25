using TiaBridge.Host.Api;

if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("{\"error\":\"WINDOWS_REQUIRED\"}"); return 2; }
if (args.Length is not (2 or 3) || args[0] != "--config" || args.Length == 3 && args[2] != "--stdio")
{ Console.Error.WriteLine("{\"error\":\"Use --config <host.json> [--stdio]\"}"); return 2; }
try { await HostApi.RunAsync(HostConfiguration.Read(Path.GetFullPath(args[1])), args.Length == 3); return 0; }
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException or TiaBridge.Host.Storage.StorageException or TiaBridge.Core.Transport.V1.ContractValidationException or InvalidOperationException)
{ Console.Error.WriteLine("{\"error\":\"HOST_START_OR_STORAGE_FAILED\"}"); return 4; }
