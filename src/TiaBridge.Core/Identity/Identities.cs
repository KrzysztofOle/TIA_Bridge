namespace TiaBridge.Core.Identity;

/// <summary>Bridge identity; never a Siemens proxy, XML identifier, name or path.</summary>
public abstract record UuidIdentity : IComparable<UuidIdentity>
{
    public Guid Value { get; }

    protected UuidIdentity(Guid value)
    {
        Value = value;
    }

    public int CompareTo(UuidIdentity? other)
    {
        if (other is null) return 1;
        int kind = string.Compare(GetType().FullName, other.GetType().FullName, StringComparison.Ordinal);
        return kind == 0 ? Value.CompareTo(other.Value) : kind;
    }

    public sealed override string ToString() => Value.ToString("D");

    public static Guid ParseValue(string value)
    {
        if (value is null || value.Length != 36 || !Guid.TryParseExact(value, "D", out Guid parsed))
            throw new ArgumentException("Expected a UUID in hyphenated format.");
        return parsed;
    }
}

public sealed record ProjectKey : UuidIdentity { public ProjectKey(Guid value) : base(value) { } }
public sealed record ObjectId : UuidIdentity { public ObjectId(Guid value) : base(value) { } }
public sealed record OperationId : UuidIdentity { public OperationId(Guid value) : base(value) { } }
public sealed record SessionEpoch : UuidIdentity { public SessionEpoch(Guid value) : base(value) { } }

/// <summary>Object identity deliberately contains no mutable locator.</summary>
public sealed record ObjectIdentity
{
    public ProjectKey Project { get; }
    public ObjectId Object { get; }

    public ObjectIdentity(ProjectKey project, ObjectId @object)
    {
        Project = project ?? throw new ArgumentNullException(nameof(project));
        Object = @object ?? throw new ArgumentNullException(nameof(@object));
    }
}

/// <summary>Observation binding; constructing it does not verify a live process.</summary>
public sealed record SessionIdentity
{
    public ProjectKey Project { get; }
    public SessionEpoch Epoch { get; }
    public int ProcessId { get; }
    public DateTimeOffset ProcessStartedAt { get; }
    public Guid HandshakeInstance { get; }

    public SessionIdentity(ProjectKey project, SessionEpoch epoch, int processId,
        DateTimeOffset processStartedAt, Guid handshakeInstance)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(epoch);
        if (processId <= 0 || processStartedAt.Offset != TimeSpan.Zero || processStartedAt == default || handshakeInstance == Guid.Empty)
            throw new ArgumentException("Invalid session observation binding.");
        Project = project;
        Epoch = epoch;
        ProcessId = processId;
        ProcessStartedAt = processStartedAt;
        HandshakeInstance = handshakeInstance;
    }
}

public sealed record ContractVersion
{
    public static ContractVersion V1 { get; } = new("1.0");
    public string Value { get; }

    public ContractVersion(string value)
    {
        if (!string.Equals(value, "1.0", StringComparison.Ordinal))
            throw new ArgumentException("Unsupported contract version.");
        Value = value;
    }

    public override string ToString() => Value;
}
