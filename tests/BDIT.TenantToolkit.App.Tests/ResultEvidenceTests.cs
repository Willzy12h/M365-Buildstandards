using System.Text.Json.Nodes;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core.Models;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class ResultEvidenceTests
{
    [Fact]
    public void Missing_historical_fields_stay_unknown_and_cannot_look_like_verified_success()
    {
        var result = new RunResult { ControlId = "SYN-001", Status = ResultStatus.Completed, Configuration = ConfigurationVerification.Unknown };
        var detail = new ResultEvidence(result, new RelayCommand(() => { }));
        Assert.Contains("Write acceptance: Unknown", detail.Summary);
        Assert.Contains("Configuration readback: Unknown", detail.Summary);
        Assert.Contains("Functional user, device or sign-in testing required", detail.Summary);
        Assert.Contains("unknown", detail.Before);
        Assert.Contains("historical", detail.Requested);
        Assert.Contains("do not replay", detail.After);
    }

    [Fact]
    public void Before_requested_and_after_show_separate_recorded_values_without_mutating_evidence()
    {
        var result = new RunResult { BeforeObject = new JsonObject { ["value"] = "before" },
            WrittenPayload = new JsonObject { ["value"] = "requested" }, AfterObject = new JsonObject { ["value"] = "after" },
            WriteAcceptance = WriteAcceptance.Accepted, Configuration = ConfigurationVerification.Pass };
        var detail = new ResultEvidence(result, new RelayCommand(() => { }));
        Assert.Contains("before", detail.Before);
        Assert.DoesNotContain("requested", detail.Before);
        Assert.Contains("requested", detail.Requested);
        Assert.Contains("after", detail.After);
        Assert.Contains("BEFORE", detail.CopyText);
        Assert.Contains("REQUESTED", detail.CopyText);
        Assert.Contains("AFTER", detail.CopyText);
        Assert.Equal("requested", result.WrittenPayload["value"]!.GetValue<string>());
    }
}
