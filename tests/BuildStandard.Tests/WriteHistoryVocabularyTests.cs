using BuildStandard.Core;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Models;
using BuildStandard.Engine.Evidence;
using Xunit;

namespace BuildStandard.Tests;

public sealed class WriteHistoryVocabularyTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("future-value", true)]
    [InlineData("accepted", true)]
    [InlineData(WriteAcceptance.Unknown, true)]
    [InlineData(WriteAcceptance.Accepted, false)]
    [InlineData(WriteAcceptance.Rejected, false)]
    [InlineData(WriteAcceptance.NotAttempted, false)]
    public void Classification_preserves_exact_vocabulary_without_claiming_accepted_proof(string? value, bool uncertain)
        => Assert.Equal(uncertain, WriteAcceptance.IsUncertain(value));

    public static IEnumerable<object[]> UncertainCases() =>
        from family in new[] { "reviewed-change", "laps", "recovery", "package" }
        from value in new[] { WriteAcceptance.Unknown, "future-value", "", " ", "unknown", "accepted" }
        select new object[] { family, value };

    [Theory]
    [MemberData(nameof(UncertainCases))]
    public void Unsupported_history_outcomes_refuse_without_rewriting_saved_evidence(string family, string value)
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        var guard = SaveHistory(store, family, value);
        var files = Directory.GetFiles(store.RootDirectory, "*.json", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllBytes);
        Assert.Throws<SafetyViolationException>(guard);
        foreach (var (file, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Theory]
    [InlineData(WriteAcceptance.NotAttempted)]
    [InlineData(WriteAcceptance.Rejected)]
    [InlineData(WriteAcceptance.Accepted)]
    public void Explicit_known_outcomes_keep_existing_family_verification_semantics(string value)
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        foreach (var family in new[] { "reviewed-change", "laps", "recovery", "package" })
            SaveHistory(store, family, value, verified: true)();
    }

    [Theory]
    [InlineData("reviewed-change")]
    [InlineData("laps")]
    [InlineData("recovery")]
    public void Accepted_history_still_requires_its_existing_readback_proof(string family)
    {
        using var root = new TempRoot();
        var store = new EvidenceStore(root.Paths, NullLog.Instance);
        Assert.Throws<SafetyViolationException>(SaveHistory(store, family, WriteAcceptance.Accepted));
    }

    private static Action SaveHistory(EvidenceStore store, string family, string value, bool verified = false)
    {
        var id = Guid.NewGuid().ToString();
        var verification = verified ? ConfigurationVerification.Pass : ConfigurationVerification.NotRun;
        const string control = "CA-001";
        switch (family)
        {
            case "reviewed-change":
                store.SaveReviewedChangeRun(new() { Id = id, TenantId = TestData.TenantA, ControlId = control,
                    WriteAcceptance = value, Verification = verification });
                return () => store.AssertNoUnresolvedReviewedChanges(TestData.TenantA);
            case "laps":
                store.SaveEntraLapsRun(new() { Id = id, TenantId = TestData.TenantA, WriteAcceptance = value, Verification = verification });
                return () => store.AssertNoUnresolvedEntraLaps(TestData.TenantA);
            case "recovery":
                store.SaveRecoveryRun(new() { Id = id, TenantId = TestData.TenantA, ControlId = control,
                    WriteAcceptance = value, Verification = verification, Status = verified ? RunStatus.Completed : RunStatus.ReviewRequired });
                return () => store.AssertNoUnresolvedRecovery(TestData.TenantA, [control]);
            case "package":
                store.SavePackageRun(new() { Id = id, TenantId = TestData.TenantA, ObjectId = "33333333-3333-4333-8333-333333333333",
                    Steps = [new() { Acceptance = value }] });
                return () => store.AssertNoUnknownPackageWrite(TestData.TenantA, "33333333-3333-4333-8333-333333333333");
            default: throw new ArgumentOutOfRangeException(nameof(family));
        }
    }
}
