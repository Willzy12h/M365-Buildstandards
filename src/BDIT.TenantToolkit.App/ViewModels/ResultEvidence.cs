using System.Windows.Input;
using BDIT.TenantToolkit.Core.Json;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>Presentation of recorded evidence only. Never derives acceptance or verification from missing data.</summary>
public sealed class ResultEvidence
{
    public ResultEvidence(RunResult? result, ICommand copyCommand)
    {
        CopyCommand = copyCommand;
        Summary = result is null ? "Select an outcome to inspect its recorded evidence." :
            $"{result.ControlId} · {result.Name}\n{result.PlannedAction} · {result.Status}\nWrite acceptance: {result.WriteAcceptance}\nConfiguration readback: {result.Configuration}\nFunctional check: {result.Verification}\nObject: {result.ObjectId ?? "not recorded"}\n{result.Reason}";
        Before = result?.BeforeObject?.ToJsonString(ToolkitJson.Options) ?? "No before object recorded here. Inspect the linked before capture; missing evidence is unknown.";
        Requested = result?.WrittenPayload?.ToJsonString(ToolkitJson.Options) ?? "No requested payload recorded here. A historical record may not contain this field; inspect its saved plan.";
        After = result?.AfterObject?.ToJsonString(ToolkitJson.Options) ?? "No after object recorded here. Inspect the after capture or use read-only re-verification; do not replay the write.";
    }

    public ICommand CopyCommand { get; }
    public string Summary { get; }
    public string Before { get; }
    public string Requested { get; }
    public string After { get; }
    public string CopyText => Summary + "\n\nBEFORE\n" + Before + "\n\nREQUESTED\n" + Requested + "\n\nAFTER\n" + After;
}
