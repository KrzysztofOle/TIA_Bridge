using System.Security.Cryptography;
using System.Text;
using Esprima;
using Esprima.Ast;

namespace TiaBridge.Core.Hmi;

public sealed record ScriptPart(string Name, string Kind, int Offset, int Length, string Sha256, byte[] Content);
public sealed record ScriptSplit(string Status, string Reason, IReadOnlyList<ScriptPart> Parts);

/// <summary>Conservative ES-module source projection. Never executes JS or interprets native YAML.</summary>
public static class HmiScriptSource
{
    public const string Profile = "hmi.es-module-prefix.v1";
    public const int MaxSourceBytes = 1024 * 1024;
    public static readonly IReadOnlyList<string> CatalogFunctions = Array.AsReadOnly(new[]
    {
        "createCatalog", "createRecipeTableModel", "directoryFailure", "getT05CatalogSchema",
        "HandleRecipeCatalog", "performCatalogAction", "render", "validateData", "validateDictionaries"
    });
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static bool SafeName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 160
        && name is not ("." or "..") && !name.EndsWith(' ') && !name.EndsWith('.')
        && !name.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
        && !new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
            .Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase);

    public static ScriptSplit Split(byte[] bytes, IReadOnlyList<string> expectedFunctions)
    {
        ScriptSplit Block(string reason) => new("BLOCKED", reason, []);
        if (bytes.Length == 0 || bytes.Length > MaxSourceBytes || expectedFunctions.Count is < 1 or > 128)
            return Block("SIZE_OR_FUNCTION_LIMIT");
        try
        {
            // Strict UTF-8 only. BOM, CRLF/LF, Unicode and source spelling are retained in byte slices.
            string source = new UTF8Encoding(false, true).GetString(bytes);
            int nodes = 0;
            var parser = new JavaScriptParser(new ParserOptions
            {
                Tolerant = false,
                MaxAssignmentDepth = 128,
                OnNodeCreated = _ => { if (++nodes > 100000) throw new InvalidOperationException("NODE_LIMIT"); }
            });
            var module = parser.ParseModule(source);
            var functions = new List<(string Name, int Start, int End)>();
            bool exported = false;
            int lastGlobalEnd = 0;
            foreach (var statement in module.Body)
            {
                if (statement is ExportNamedDeclaration { Declaration: FunctionDeclaration { Id: not null } f } e)
                {
                    if (!exported && source.AsSpan(lastGlobalEnd, e.Range.Start - lastGlobalEnd).ContainsAnyExcept(" \t\r\n\uFEFF"))
                        return Block("AMBIGUOUS_GLOBAL_FUNCTION_BOUNDARY");
                    exported = true;
                    functions.Add((f.Id.Name, e.Range.Start, e.Range.End));
                }
                else if (exported || statement is ExportNamedDeclaration or ExportDefaultDeclaration or ExportAllDeclaration)
                    return Block("UNSUPPORTED_TOP_LEVEL_STRUCTURE");
                else lastGlobalEnd = statement.Range.End;
            }
            if (functions.Count != expectedFunctions.Count
                || !functions.Select(f => f.Name).Order(StringComparer.Ordinal).SequenceEqual(expectedFunctions.Order(StringComparer.Ordinal)))
                return Block("INCOMPLETE_OR_UNEXPECTED_FUNCTION_INVENTORY");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Global definition area" };
            if (functions.Any(f => !SafeName(f.Name) || !names.Add(f.Name))) return Block("NAME_COLLISION_OR_UNSAFE_NAME");
            var parts = new List<ScriptPart>();
            int ByteOffset(int index) => Encoding.UTF8.GetByteCount(source.AsSpan(0, index));
            void Add(string name, string kind, int start, int end)
            {
                int offset = ByteOffset(start), length = ByteOffset(end) - offset;
                byte[] content = bytes.AsSpan(offset, length).ToArray();
                parts.Add(new(name, kind, offset, length, Hash(content), content));
            }
            Add("Global definition area", "GLOBAL_PREFIX", 0, functions[0].Start);
            for (int i = 0; i < functions.Count; i++)
            {
                var f = functions[i]; int end = i + 1 < functions.Count ? functions[i + 1].Start : source.Length;
                // A comment/statement between declarations has ambiguous ownership in the TIA editor.
                // Refuse it rather than silently assigning code to a different native function/area.
                if (source.AsSpan(f.End, end - f.End).ContainsAnyExcept(" \t\r\n")) return Block("AMBIGUOUS_INTER_FUNCTION_CONTENT");
                Add(f.Name, "EXPORTED_FUNCTION", f.Start, end);
            }
            if (!parts.SelectMany(p => p.Content).SequenceEqual(bytes)) return Block("RECONSTRUCTION_MISMATCH");
            return new("PASS", "BYTE_EXACT_SOURCE_PROJECTION_NATIVE_FIELD_MAPPING_UNQUALIFIED", parts);
        }
        catch (Exception e) when (e is ParserException or DecoderFallbackException or InvalidOperationException or InsufficientExecutionStackException)
        { return Block("UNSUPPORTED_OR_INVALID_JAVASCRIPT"); }
    }
}
