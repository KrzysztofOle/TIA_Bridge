using System.Diagnostics;

namespace TiaBridge.Host.Api;

/// <summary>Bounded admission with no waiting list. Independent quotas reserve access for status/health.</summary>
internal sealed class RequestGate(int requestsPerSecond, int submitsPerSecond, int concurrent)
{
    private readonly object sync = new();
    private long second;
    private int requests, submits, active, activeSubmits;
    internal IDisposable? Enter(bool submit)
    {
        lock (sync)
        {
            long now = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
            if (now != second) { second = now; requests = 0; submits = 0; }
            if (requests >= requestsPerSecond || submit && (submits >= submitsPerSecond || activeSubmits >= Math.Max(1, concurrent / 2)) || active >= concurrent) return null;
            requests++; if (submit) { submits++; activeSubmits++; }
            active++; return new Lease(this, submit);
        }
    }
    private sealed class Lease(RequestGate gate, bool submit) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) lock (gate.sync) { gate.active--; if (submit) gate.activeSubmits--; } }
    }
}
