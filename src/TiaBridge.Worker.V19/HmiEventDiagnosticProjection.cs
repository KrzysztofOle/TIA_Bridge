using System.Security.Cryptography;
using System.Text;

namespace TiaBridge.Worker.V19;

// Internal projection boundary for the bounded diagnostic V19 reader.
// This type is not wired to IPC, Host dispatch, or a capability profile.
internal static class HmiEventDiagnosticProjection
{
    internal static HmiEventDiagnosticRecord Project(
        string[] locator,
        string? objectName,
        string? objectType,
        string? eventName,
        string? vendorEventType,
        bool eventEnumerationComplete,
        bool handlerPresent,
        string? scriptCode,
        string? globalDefinitionAreaScriptCode,
        string? error = null,
        string? scriptError = null,
        string? globalDefinitionAreaError = null,
        bool eventKindKnown = false)
    {
        string metadataStatus = locator.Length > 0 && locator.All(IsValidText)
            && IsValidText(objectName) && IsValidText(objectType) && IsValidText(eventName)
            && (IsValidText(vendorEventType) || (!handlerPresent && eventKindKnown))
            ? "OBSERVED" : "UNKNOWN";

        string handlerStatus;
        string codeStatus;
        string? codeHash = null;
        string? globalHash = null;
        string? safeError = error is null ? null : BoundError(error);

        if (metadataStatus != "OBSERVED")
        {
            handlerStatus = "UNKNOWN";
            codeStatus = "UNKNOWN";
        }
        else if (error is not null)
        {
            handlerStatus = handlerPresent ? "PRESENT" : "UNKNOWN";
            codeStatus = "READ_FAILED";
        }
        else if (!eventEnumerationComplete && !handlerPresent)
        {
            handlerStatus = "UNKNOWN";
            codeStatus = "UNKNOWN";
        }
        else if (!handlerPresent)
        {
            handlerStatus = "ABSENT";
            codeStatus = "NOT_APPLICABLE";
        }
        else if (scriptError is not null)
        {
            handlerStatus = "PRESENT";
            codeStatus = "READ_FAILED";
        }
        else if (scriptCode is null)
        {
            handlerStatus = "PRESENT";
            codeStatus = "UNKNOWN";
        }
        else if (scriptCode.Length == 0)
        {
            handlerStatus = "PRESENT_EMPTY";
            codeStatus = "EMPTY";
            codeHash = Hash(scriptCode);
        }
        else
        {
            handlerStatus = "PRESENT";
            codeStatus = "READ";
            codeHash = Hash(scriptCode);
        }

        string globalStatus = metadataStatus != "OBSERVED" || !handlerPresent || error is not null
            ? "UNKNOWN"
            : globalDefinitionAreaError is not null ? "READ_FAILED"
            : globalDefinitionAreaScriptCode is null ? "UNKNOWN"
            : globalDefinitionAreaScriptCode.Length == 0 ? "EMPTY" : "READ";
        bool contentObserved = !handlerPresent || ((codeStatus is "READ" or "EMPTY")
            && (globalStatus is "READ" or "EMPTY"));
        if (handlerPresent && globalStatus is "READ" or "EMPTY") globalHash = Hash(globalDefinitionAreaScriptCode!);
        string coverage = eventEnumerationComplete && metadataStatus == "OBSERVED" && error is null
            && scriptError is null && globalDefinitionAreaError is null && contentObserved
            ? "COMPLETE_FOR_EVENT_ENUMERATION" : "PARTIAL";

        return new HmiEventDiagnosticRecord(
            locator.ToArray(), objectName, objectType, eventName, vendorEventType,
            metadataStatus, handlerStatus, codeStatus, scriptCode, codeHash,
            globalStatus, globalDefinitionAreaScriptCode, globalHash, coverage, safeError,
            scriptError is null ? null : BoundError(scriptError),
            globalDefinitionAreaError is null ? null : BoundError(globalDefinitionAreaError));
    }

    private static bool IsValidText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)))
            .Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string BoundError(string value)
    {
        string sanitized = new(value.Where(c => !char.IsControl(c) || c is '\t').Take(256).ToArray());
        return sanitized.Length == 0 ? "UNSPECIFIED_READ_ERROR" : sanitized;
    }
}

internal sealed class HmiEventDiagnosticRecord
{
    internal HmiEventDiagnosticRecord(string[] locator, string? objectName, string? objectType,
        string? eventName, string? vendorEventType, string metadataStatus, string handlerStatus,
        string codeStatus, string? scriptCode, string? scriptCodeSha256,
        string globalDefinitionAreaStatus, string? globalDefinitionAreaScriptCode,
        string? globalDefinitionAreaSha256, string coverage, string? error,
        string? scriptError, string? globalDefinitionAreaError)
    {
        Locator = locator; ObjectName = objectName; ObjectType = objectType; EventName = eventName;
        VendorEventType = vendorEventType; MetadataStatus = metadataStatus; HandlerStatus = handlerStatus;
        CodeStatus = codeStatus; ScriptCode = scriptCode; ScriptCodeSha256 = scriptCodeSha256;
        GlobalDefinitionAreaStatus = globalDefinitionAreaStatus;
        GlobalDefinitionAreaScriptCode = globalDefinitionAreaScriptCode;
        GlobalDefinitionAreaSha256 = globalDefinitionAreaSha256; Coverage = coverage; Error = error;
        ScriptError = scriptError; GlobalDefinitionAreaError = globalDefinitionAreaError;
    }

    internal string[] Locator { get; }
    internal string? ObjectName { get; }
    internal string? ObjectType { get; }
    internal string? EventName { get; }
    internal string? VendorEventType { get; }
    internal string MetadataStatus { get; }
    internal string HandlerStatus { get; }
    internal string CodeStatus { get; }
    internal string? ScriptCode { get; }
    internal string? ScriptCodeSha256 { get; }
    internal string GlobalDefinitionAreaStatus { get; }
    internal string? GlobalDefinitionAreaScriptCode { get; }
    internal string? GlobalDefinitionAreaSha256 { get; }
    internal string Coverage { get; }
    internal string? Error { get; }
    internal string? ScriptError { get; }
    internal string? GlobalDefinitionAreaError { get; }
}
