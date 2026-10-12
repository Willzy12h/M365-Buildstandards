using BuildStandard.Core;
using BuildStandard.Core.Safety;
using Xunit;

namespace BuildStandard.Tests;

public sealed class ReviewedWriteAdmissionTests
{
    [Theory]
    [InlineData(1199, true)][InlineData(1200, true)][InlineData(1201, false)]
    public void Original_snapshot_and_plan_limit_is_not_extended(int seconds, bool allowed)
    {
        var clock = new FixedClock(); var original = clock.UtcNow;
        var admission = new ReviewedWriteAdmission(original, original, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20), clock, () => { }, () => false);
        clock.UtcNow = original.AddSeconds(seconds);
        if (allowed) admission.AssertAllowed();
        else Assert.Throws<PlanValidationException>(admission.AssertAllowed);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public void Either_original_deadline_can_refuse(bool snapshotExpires)
    {
        var clock = new FixedClock(); var original = clock.UtcNow;
        var admission = new ReviewedWriteAdmission(original, original,
            TimeSpan.FromMinutes(snapshotExpires ? 1 : 20), TimeSpan.FromMinutes(snapshotExpires ? 20 : 1), clock, () => { }, () => false);
        clock.UtcNow = original.AddMinutes(2);
        Assert.Throws<PlanValidationException>(admission.AssertAllowed);
    }

    [Fact]
    public void A_slow_binding_check_cannot_hide_expiry()
    {
        var clock = new FixedClock(); var original = clock.UtcNow;
        var admission = new ReviewedWriteAdmission(original, original, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20), clock,
            () => clock.UtcNow = original.AddMinutes(21), () => false);
        Assert.Throws<PlanValidationException>(admission.AssertAllowed);
    }

    [Fact]
    public void Changed_binding_and_stop_are_not_authority_to_send()
    {
        var clock = new FixedClock(); var original = clock.UtcNow;
        var admission = new ReviewedWriteAdmission(original, original, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20), clock,
            () => throw new PlanValidationException("Changed reviewed binding"), () => false);
        Assert.Throws<PlanValidationException>(admission.AssertAllowed);
        var stop = false;
        admission = new ReviewedWriteAdmission(original, original, TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(20), clock,
            () => stop = true, () => stop);
        Assert.Throws<OperationCanceledException>(admission.AssertAllowed);
    }
}
