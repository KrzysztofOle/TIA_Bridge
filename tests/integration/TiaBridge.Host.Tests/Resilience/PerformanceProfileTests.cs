using System.Text.Json;
using Xunit;

namespace TiaBridge.Host.Tests;

public class PerformanceProfileTests
{
    [Fact]
    public void Strict_remains_default_and_shared_profile_changes_only_versioned_performance_limits()
    {
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
        using var definitions = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository, "tests/resilience/performance-profiles.json")));
        Assert.Equal("STRICT", definitions.RootElement.GetProperty("default_profile").GetString());
        var strict = QualificationProfile.Load(repository, "STRICT");
        var shared = QualificationProfile.Load(repository, "SHARED_WORKSTATION");
        Assert.Equal(100, strict.Number("nf03_replace_p95_ms"));
        Assert.Equal(500, shared.Number("nf03_replace_p95_ms"));
        Assert.Equal(10, strict.HarnessNumber("fake_worker_phase_observation_s"));
        Assert.Equal(30, shared.HarnessNumber("fake_worker_phase_observation_s"));
        Assert.Equal(250, shared.Number("nf01_status_p95_ms"));
        Assert.Equal(500, shared.Number("nf02_submit_p95_ms"));
        Assert.Equal(10000, shared.Number("nf05_startup_p95_ms"));
        Assert.Equal(1, strict.Number("nf08_host_idle_cpu_mean_percent"));
        Assert.Equal(2, shared.Number("nf08_host_idle_cpu_mean_percent"));
        Assert.Equal(strict.Number("nf08_host_ram_mib"), shared.Number("nf08_host_ram_mib"));
        Assert.Equal(strict.Number("nf08_worker_ram_mib"), shared.Number("nf08_worker_ram_mib"));
    }

    [Fact]
    public void Profile_definition_has_no_api_or_permission_or_timeout_controls()
    {
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../"));
        using var definitions = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(repository, "tests/resilience/performance-profiles.json")));
        JsonElement policy = definitions.RootElement;
        Assert.Equal(300, policy.GetProperty("unchanged_protective_limits").GetProperty("queue_wait_default_s").GetInt32());
        Assert.Equal(1000, policy.GetProperty("unchanged_protective_limits").GetProperty("max_active_operations").GetInt32());
        Assert.Equal(8, policy.GetProperty("unchanged_protective_limits").GetProperty("nf11_soak_minimum_h").GetInt32());
        Assert.False(policy.GetProperty("profiles").GetProperty("SHARED_WORKSTATION").TryGetProperty("capabilities", out _));
        Assert.False(policy.GetProperty("profiles").GetProperty("SHARED_WORKSTATION").TryGetProperty("authorization", out _));
    }
}
