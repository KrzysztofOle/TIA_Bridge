using System.Text;
using System.Text.RegularExpressions;
using TiaBridge.Core.Identity;

namespace TiaBridge.Core.Workspace;

/// <summary>Document 04 percent-UTF8/NFC presentation. Metadata is required for lossless decoding.</summary>
public static class WorkspaceNameCodec
{
    public const int MaxSegmentLength = 100;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Encode(string originalName, ObjectId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrEmpty(originalName);
        if (originalName.Length > 32768) throw new ArgumentException("Name limit exceeded.");
        try { _ = Utf8.GetByteCount(originalName); }
        catch (EncoderFallbackException) { throw new ArgumentException("Invalid Unicode name."); }
        string nfc = originalName.Normalize(NormalizationForm.FormC);
        var tokens = new List<string>();
        foreach (Rune rune in nfc.EnumerateRunes())
        {
            string token = rune.ToString();
            if (rune.Value < 32 || rune.Value == 127 || "<>:\"/\\|?*%".Contains(token, StringComparison.Ordinal))
                token = string.Concat(Utf8.GetBytes(token).Select(b => $"%{b:X2}"));
            tokens.Add(token);
        }
        // Prefix before truncation; escaped separators and percent signs are never decoded as paths.
        string prefix = WorkspacePath.IsReserved(nfc) || nfc.EndsWith(' ') || nfc.EndsWith('.') ? "_" : "";
        string suffix = "--" + id.Value.ToString("N")[^12..];
        var result = new StringBuilder(prefix);
        foreach (string token in tokens)
        {
            if (result.Length + token.Length + suffix.Length > MaxSegmentLength) break;
            result.Append(token);
        }
        // Suffix also makes a truncated trailing space/dot safe on Windows.
        return result.Append(suffix).ToString();
    }

    public static string Decode(string segment, string originalName, ObjectId id)
    {
        if (!string.Equals(segment, Encode(originalName, id), StringComparison.Ordinal))
            throw new ArgumentException("Name metadata does not match encoded segment.");
        return originalName;
    }
}

public static class WorkspacePath
{
    public const int MaxPathLength = 240;
    private static readonly Regex Reserved = new(@"\A(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|CLOCK\$|COM[1-9¹²³]|LPT[1-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool IsReserved(string name) => Reserved.IsMatch(name)
        || Reserved.IsMatch(name.Split('.')[0].TrimEnd(' '));

    public static void ValidateRelative(string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        if (relativePath.Length > MaxPathLength || relativePath.Contains('\\')) throw new ArgumentException("Invalid relative path.");
        foreach (string segment in relativePath.Split('/'))
        {
            if (segment.Length is 0 or > WorkspaceNameCodec.MaxSegmentLength || segment is "." or ".."
                || segment.EndsWith(' ') || segment.EndsWith('.') || IsReserved(segment)
                || !segment.IsNormalized(NormalizationForm.FormC)
                || segment.Any(c => c < 32 || c == 127 || "<>:\"\\|?*".Contains(c)))
                throw new ArgumentException("Unsafe workspace path.");
            _ = new UTF8Encoding(false, true).GetByteCount(segment);
        }
    }

    public static string ValidateRoot(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        if (root.Length < 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\'
            || root.Contains('/') || root.Length > MaxPathLength)
            throw new ArgumentException("Expected a local absolute Windows root.");
        string normalized = root.TrimEnd('\\');
        if (normalized.Length > 2) ValidateRelative(normalized[3..].Replace('\\', '/'));
        return normalized + "\\";
    }

    public static string Resolve(string root, string relativePath)
    {
        string prefix = ValidateRoot(root);
        ValidateRelative(relativePath);
        string result = prefix + relativePath.Replace('/', '\\');
        if (result.Length > MaxPathLength) throw new ArgumentException("Workspace path limit exceeded.");
        return result;
    }

    public static void ValidateUnique(IEnumerable<string> paths)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            ValidateRelative(path);
            if (!files.Add(path)) throw new ArgumentException("Colliding workspace paths.");
            string current = "";
            foreach (string segment in path.Split('/'))
            {
                current = current.Length == 0 ? segment : current + "/" + segment;
                if (spellings.TryGetValue(current, out string? previous) && previous != current)
                    throw new ArgumentException("Colliding directory spellings.");
                spellings[current] = current;
            }
        }
        foreach (string path in files)
        {
            int index = path.IndexOf('/');
            while (index >= 0)
            {
                if (files.Contains(path[..index])) throw new ArgumentException("File/directory collision.");
                index = path.IndexOf('/', index + 1);
            }
        }
    }

    /// <summary>Requires observations of every component from volume root through target, including
    /// ancestors of the configured workspace. No filesystem IO, freshness check or safety receipt.</summary>
    public static void ValidateObservations(string root, string relativePath, IReadOnlyList<PathObservation> observations)
    {
        string resolved = Resolve(root, relativePath);
        string[] parts = resolved[3..].Split('\\');
        string[] expected = new[] { resolved[..3] }.Concat(Enumerable.Range(1, parts.Length).Select(i => resolved[..3] + string.Join('\\', parts.Take(i)))).ToArray();
        if (observations.Count != expected.Length) throw new ArgumentException("Incomplete path observations.");
        for (int i = 0; i < expected.Length; i++)
            if (observations[i].AbsolutePath != expected[i] || (observations[i].Attributes & FileAttributes.ReparsePoint) != 0
                || i < expected.Length - 1 && (observations[i].Attributes & FileAttributes.Directory) == 0)
                throw new ArgumentException("Unsafe or incomplete path observations.");
    }
}

public sealed record PathObservation(string AbsolutePath, FileAttributes Attributes);
public sealed record WorkspacePathNames(string DeviceName, IReadOnlyList<string> GroupNames, string ObjectName);

/// <summary>Logical layout only. An exchange unit may span many such objects and files.</summary>
public static class WorkspaceLayout
{
    public static WorkspacePathNames DecodeObjectDirectory(WorkspaceProject project, WorkspaceObject item, string relativeDirectory)
    {
        WorkspacePath.ValidateRelative(relativeDirectory);
        if (relativeDirectory != ObjectDirectory(project, item)) throw new ArgumentException("Path metadata mismatch.");
        string[] segments = relativeDirectory.Split('/');
        var device = project.Devices.Single(d => d.DeviceId == item.VendorLocator.DeviceId);
        string deviceName = WorkspaceNameCodec.Decode(segments[1], device.OriginalName, device.DeviceId);
        var groupNames = item.VendorLocator.Groups.Select((id, index) =>
        {
            var group = project.Groups.Single(g => g.GroupId == id);
            return WorkspaceNameCodec.Decode(segments[index + 2], group.OriginalName, group.GroupId);
        }).ToArray();
        string objectName = WorkspaceNameCodec.Decode(segments[^1], item.VendorLocator.OriginalName, item.ObjectId);
        return new(deviceName, groupNames, objectName);
    }

    public static string DeviceDirectory(WorkspaceDevice device) => (device.Family == DeviceFamily.PLC ? "PLC/" : "HMI/")
        + WorkspaceNameCodec.Encode(device.OriginalName, device.DeviceId);

    public static string GroupDirectory(WorkspaceProject project, WorkspaceGroup group)
    {
        var segments = new List<string>();
        var visited = new HashSet<ObjectId>();
        WorkspaceGroup? current = group;
        while (current is not null)
        {
            if (!visited.Add(current.GroupId) || current.DeviceId != group.DeviceId) throw new ArgumentException("Invalid group hierarchy.");
            segments.Add(WorkspaceNameCodec.Encode(current.OriginalName, current.GroupId));
            current = current.ParentGroupId is null ? null : project.Groups.Single(g => g.GroupId == current.ParentGroupId);
        }
        segments.Reverse();
        string path = DeviceDirectory(project.Devices.Single(d => d.DeviceId == group.DeviceId)) + "/" + string.Join('/', segments);
        WorkspacePath.ValidateRelative(path);
        return path;
    }

    public static string ObjectDirectory(WorkspaceProject project, WorkspaceObject item)
    {
        WorkspaceDevice device = project.Devices.Single(d => d.DeviceId == item.VendorLocator.DeviceId);
        var segments = new List<string> { DeviceDirectory(device) };
        segments.AddRange(item.VendorLocator.Groups.Select(id => project.Groups.Single(g => g.GroupId == id))
            .Select(g => WorkspaceNameCodec.Encode(g.OriginalName, g.GroupId)));
        segments.Add(WorkspaceNameCodec.Encode(item.VendorLocator.OriginalName, item.ObjectId));
        string path = string.Join('/', segments);
        WorkspacePath.ValidateRelative(path);
        return path;
    }
}
