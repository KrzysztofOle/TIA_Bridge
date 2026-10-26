using TiaBridge.Core.Capabilities;
using TiaBridge.Core.Operations;
using TiaBridge.Core.Workspace;
using Xunit;

namespace TiaBridge.Core.Tests;

public sealed class QualificationEvidenceTests
{
    private static readonly string[] Getters = ["Project.Path", "Project.Devices"];
    private static readonly QualificationRequirement Required = new("evidence-1", "19.0.3.0", "adapter.v19", "adapter-build-7",
        "worker-build-9", OperationMethod.ProjectInspect, OperationScope.PROJECT, null, "project", "v19.discovery.v1",
        Getters, [CoverageDimension.STRUCTURE], "11111111-1111-4111-8111-111111111111", null);
    private static QualificationEvidence Evidence(QualificationResult result = QualificationResult.PASS) => new("evidence-1", 1,
        QualificationEvidenceKind.ENGINEERING_TEST, "qualified-run report", "operator review record", "19.0.3.0", "adapter.v19",
        "adapter-build-7", "worker-build-9", OperationMethod.ProjectInspect, OperationScope.PROJECT, null, "project",
        "v19.discovery.v1", Getters, [new(CoverageDimension.STRUCTURE, CoverageState.COMPLETE)], result, "run-1", Required.ProjectKey, null,
        [new("report.json", new string('a', 64))], DateTimeOffset.Parse("2026-10-26T12:00:00Z"));

    [Fact]
    public void Exact_signed_payload_scope_build_profile_read_coverage_and_project_are_eligible()
    {
        var assessment = QualificationEvidencePolicy.Assess(Required, [Evidence()]);
        Assert.True(assessment.Qualified);
        Assert.Equal(QualificationEvidenceDenial.NONE, assessment.Denial);
    }

    [Theory]
    [InlineData(QualificationResult.FAIL)]
    [InlineData(QualificationResult.NOT_RUN)]
    [InlineData(QualificationResult.BLOCKED)]
    public void Non_pass_results_never_qualify(QualificationResult result) =>
        Assert.Equal(QualificationEvidenceDenial.RESULT_NOT_PASS, QualificationEvidencePolicy.Assess(Required, [Evidence(result)]).Denial);

    [Fact]
    public void Missing_or_unrequested_evidence_id_does_not_qualify() =>
        Assert.Equal(QualificationEvidenceDenial.NOT_FOUND, QualificationEvidencePolicy.Assess(Required, []).Denial);

    [Theory]
    [InlineData("TiaBuild")]
    [InlineData("AdapterBuild")]
    [InlineData("WorkerBuild")]
    [InlineData("Operation")]
    [InlineData("Scope")]
    [InlineData("DeviceFamily")]
    [InlineData("ObjectKind")]
    [InlineData("Profile")]
    [InlineData("ReadScope")]
    [InlineData("ProjectKey")]
    [InlineData("Coverage")]
    public void Any_scope_or_build_drift_is_denied(string field)
    {
        var evidence = Evidence();
        evidence = field switch
        {
            "TiaBuild" => evidence with { TiaBuild = "19.0.4.0" },
            "AdapterBuild" => evidence with { AdapterBuild = "other-adapter" },
            "WorkerBuild" => evidence with { WorkerBuild = "other-worker" },
            "Operation" => evidence with { Operation = OperationMethod.ProjectExport },
            "Scope" => evidence with { Scope = OperationScope.DEVICE, DeviceFamily = DeviceFamily.PLC },
            "DeviceFamily" => evidence with { DeviceFamily = DeviceFamily.PLC },
            "ObjectKind" => evidence with { ObjectKind = "device" },
            "Profile" => evidence with { Profile = "other-profile" },
            "ReadScope" => evidence with { ReadScope = ["Project.Path"] },
            "ProjectKey" => evidence with { ProjectKey = "22222222-2222-4222-8222-222222222222" },
            "Coverage" => evidence with { Coverage = [new(CoverageDimension.STRUCTURE, CoverageState.PARTIAL)] },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.False(QualificationEvidencePolicy.Assess(Required, [evidence]).Qualified);
    }

    [Fact]
    public void Evidence_is_not_semantically_valid_without_artifact_hash_or_with_invalid_coverage()
    {
        Assert.False(QualificationEvidencePolicy.Valid(Evidence() with { Artifacts = [new("report.json", "not-a-hash")] }));
        Assert.False(QualificationEvidencePolicy.Valid(Evidence() with { Coverage = [new((CoverageDimension)999, CoverageState.COMPLETE)] }));
    }

    [Fact]
    public void Project_scope_does_not_inherit_device_or_write_qualifications()
    {
        Assert.False(QualificationEvidencePolicy.Assess(Required with { Scope = OperationScope.DEVICE, DeviceFamily = DeviceFamily.PLC }, [Evidence()]).Qualified);
        Assert.False(QualificationEvidencePolicy.Assess(Required with { Operation = OperationMethod.ProjectExport }, [Evidence()]).Qualified);
    }
}
