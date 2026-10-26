using System.Security.Cryptography;

namespace TiaBridge.Ipc
{
    internal static class Compatibility
    {
        internal static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
        internal static bool FullyQualified(string path) => path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
        internal static byte[] Sha256(byte[] bytes) { using var hash = SHA256.Create(); return hash.ComputeHash(bytes); }
        internal static byte[] Mac(byte[] key, byte[] bytes) { using var hash = new HMACSHA256(key); return hash.ComputeHash(bytes); }
        internal static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        internal static bool FixedEquals(byte[] left, byte[] right)
        {
            int difference = left.Length ^ right.Length;
            for (int i = 0; i < left.Length && i < right.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
#if NETFRAMEWORK
        internal static async Task ReadExactlyAsync(this Stream stream, byte[] bytes, CancellationToken ct)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await stream.ReadAsync(bytes, offset, bytes.Length - offset, ct).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }
        internal static void ReadExactly(this Stream stream, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length) { int read = stream.Read(bytes, offset, bytes.Length - offset); if (read == 0) throw new EndOfStreamException(); offset += read; }
        }
        internal static Task WriteAsync(this Stream stream, byte[] bytes, CancellationToken ct) => stream.WriteAsync(bytes, 0, bytes.Length, ct);
        internal static ValueTask DisposeAsync(this Stream stream) { stream.Dispose(); return default; }
#endif
    }
}
#if NETFRAMEWORK
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }
#endif
