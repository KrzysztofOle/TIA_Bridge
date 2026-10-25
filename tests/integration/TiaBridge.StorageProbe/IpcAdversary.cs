using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaBridge.Ipc;

internal static class IpcAdversary
{
    internal static async Task<int> Run(string attack)
    {
        // Credentials are inherited via stdin and never printed or supplied as argv.
        string line = await Console.In.ReadLineAsync() ?? throw new IOException();
        if (line.Length > 16384) return 2;
        var b = JsonSerializer.Deserialize<Bootstrap>(line, Protocol.Json)!;
        using var pipe = new NamedPipeClientStream(".", b.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.ConnectAsync(deadline.Token);
        var m = new WireMessage(attack == "version" ? "2.0" : "1.0", attack == "epoch" ? Guid.NewGuid() : b.WorkerEpoch,
            attack == "sequence" ? 2 : 1, MessageKind.Heartbeat, null, 0, Protocol.Body(new { synthetic = true }));
        byte[] json = attack == "json" ? Encoding.UTF8.GetBytes("{broken") : JsonSerializer.SerializeToUtf8Bytes(m, Protocol.Json);
        byte[] frame = new byte[json.Length + 36]; BinaryPrimitives.WriteInt32LittleEndian(frame, attack == "oversize" ? Protocol.MaxFrameBytes + 1 : json.Length + 32);
        json.CopyTo(frame, 4); HMACSHA256.HashData(Convert.FromBase64String(b.Secret), json).CopyTo(frame, json.Length + 4);
        if (attack == "hmac") frame[^1] ^= 1;
        try { await pipe.WriteAsync(attack == "truncated" ? frame.AsMemory(0, frame.Length - 1) : frame.AsMemory(), deadline.Token); await pipe.FlushAsync(deadline.Token); }
        catch (IOException) { } // Peer can reject immediately after the prefix/identity check.
        return 0;
    }
}
