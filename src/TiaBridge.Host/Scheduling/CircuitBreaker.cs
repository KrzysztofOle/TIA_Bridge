namespace TiaBridge.Host.Scheduling;

public sealed record CircuitState(int Failures, DateTimeOffset? WindowStartedAt, DateTimeOffset? OpenedAt, bool HalfOpen);
/// <summary>One scheduler owner persists each change under a portal/capability key.</summary>
public sealed class CircuitBreaker(CircuitState? state = null)
{
    public CircuitState State { get; private set; } = state ?? new(0, null, null, false);
    public bool TryEnter(DateTimeOffset now, bool qualifiedRead)
    {
        if (State.HalfOpen) return false;
        if (State.OpenedAt is not { } at) return true;
        if (now - at < TimeSpan.FromSeconds(60) || !qualifiedRead) return false;
        State = State with { HalfOpen = true }; return true;
    }
    public void Success() => State = new(0, null, null, false);
    public void Failure(DateTimeOffset now)
    {
        bool newWindow = State.WindowStartedAt is not { } start || now - start > TimeSpan.FromSeconds(60);
        int count = newWindow ? 1 : State.Failures + 1;
        State = new(count, newWindow ? now : State.WindowStartedAt, count >= 3 || State.HalfOpen ? now : null, false);
    }
}
