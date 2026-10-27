using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaBridge.Host.Api;
using TiaBridge.Host.Qualification;
using TiaBridge.Host.Scheduling;
using TiaBridge.Qualification;

if (!OperatingSystem.IsWindows()) return 2;
Console.OutputEncoding = new UTF8Encoding(false);
try
{
    if (args.Length < 3 || args[0] != "--config") throw new ArgumentException();
    var config = HostConfiguration.Read(Path.GetFullPath(args[1]));
    var tool = new LocalEnrollment(config); string command = args[2];
    if (command == "list" && args.Length == 3)
    { Print(tool.List().Select(a => new { a.Evidence, a.EnvelopeSha256 })); return 0; }
    if (args.Length < 5 || !int.TryParse(args[4], out int revision)) throw new ArgumentException();
    string id = args[3];
    if (command == "inspect" && args.Length == 5)
    {
        var package = tool.Inspect(id, revision); Print(new { package.Evidence, package.PackageSha256, package.Eligible }); return 0;
    }
    if (command == "approve" && args.Length is 5 or 8)
    {
        var package = tool.Inspect(id, revision); Print(new { package.Evidence, package.PackageSha256, package.Eligible });
        if (args.Length != 8 || args[5] != "--package-sha256" || args[7] != "--confirm-operator-approval") throw new IOException("OPERATOR_APPROVAL_REQUIRED");
        Print(tool.Approve(id, revision, args[6], explicitApproval: true)); return 0;
    }
    if (command == "enroll" && args.Length == 7 && args[5] == "--key-name")
    {
        LocalTrustAcl.Operator(tool.Store.Policy);
        // Only this operator executable opens a user CNG private key. Never generated or exported here.
        using var key = CngKey.Open(args[6], CngProvider.MicrosoftSoftwareKeyStorageProvider, CngKeyOpenOptions.UserKey);
        if (key.Algorithm != CngAlgorithm.ECDsaP256 || key.IsMachineKey || key.ExportPolicy != CngExportPolicies.None) throw new IOException("USER_NONEXPORTABLE_P256_KEY_REQUIRED");
        using var signer = new ECDsaCng(key); Print(tool.Enroll(id, revision, signer)); return 0;
    }
    if (command == "verify" && args.Length == 5)
    { Print(tool.Store.Verify(id, revision, QualificationEvidenceProvider.ReadAnchors(tool.Store.Policy)).Evidence); return 0; }
    if (command == "revoke" && args.Length == 7 && args[5] == "--reason")
    { Print(tool.Revoke(id, revision, args[6])); return 0; }
    throw new ArgumentException();
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or FormatException
    or JsonException or CryptographicException or InvalidOperationException or TiaBridge.Host.Storage.StorageException or TiaBridge.Ipc.ProtocolException)
{ Console.WriteLine("{\"error\":\"LOCAL_QUALIFICATION_DENIED\"}"); return 3; }

static void Print<T>(T value)
{
    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
    Console.WriteLine(JsonSerializer.Serialize(value, LocalTrustStore.Json));
}
