using System.Collections.Immutable;
using System.Text.Json;
using TiaBridge.Core.Identity;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Transport.V1;
using Xunit;

namespace TiaBridge.Core.Tests;

public class RequiredNullableTests
{
    [Fact]
    public void Required_nullable_diagnostic_age_is_preserved_in_typed_serialization()
    {
        var report = new OperationReport("1.0", new OperationId(Guid.NewGuid()), OperationMethod.DiagnosticsHealth,
            OperationState.SUCCEEDED, "METADATA", OperationOutcome.CONFIRMED, false, 0, ImmutableArray<ArtifactRef>.Empty,
            Result: new DiagnosticsHealthResult([new ComponentHealth("tia_probe", ComponentHealth_Status.UNKNOWN, null)], DateTimeOffset.UtcNow));
        byte[] bytes = ContractJson.WriteReport(report);
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("result").GetProperty("components")[0].GetProperty("age_ms").ValueKind);
        Assert.Null(Assert.IsType<DiagnosticsHealthResult>(ContractJson.ReadReport(bytes).Result).Components[0].AgeMs);
    }
}
