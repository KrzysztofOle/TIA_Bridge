using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TiaBridge.Ipc;

public static class PipePeer
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    public static void Verify(PipeStream pipe, int expectedPid, bool server)
    {
        uint pid;
        bool ok = server ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out pid) : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out pid);
        if (!ok || pid != expectedPid) throw new ProtocolException();
#if NETFRAMEWORK
        using var process = System.Diagnostics.Process.GetProcessById(expectedPid);
        if (!OpenProcessToken(process.Handle, 8, out var token)) throw new ProtocolException();
        try
        {
            using var identity = new System.Security.Principal.WindowsIdentity(token);
            using var current = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (identity.User != current.User) throw new ProtocolException();
        }
        finally { CloseHandle(token); }
#endif
    }
#if NETFRAMEWORK
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
#endif
}
