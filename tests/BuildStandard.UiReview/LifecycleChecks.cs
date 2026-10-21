using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildStandard.App.ViewModels;
using BuildStandard.App.Views;
using BuildStandard.App.Services;
using BuildStandard.Core;
using BuildStandard.Core.Configuration;
using BuildStandard.Core.Diagnostics;
using BuildStandard.Core.Graph;
using BuildStandard.Core.Models;
using BuildStandard.Core.Safety;
using BuildStandard.Engine.Standards;
using System.Reflection;
using System.Text.Json.Nodes;

internal static partial class Program
{
    private sealed class NativeLifecycleSource(TenantSession session, LifecycleOperation operation, Action current) : ILifecycleReadSource
    {
        public string TenantId=>session.TenantId;
        public string AccountObjectId=>session.AccountObjectId;
        public string ClientId=>session.ClientId;
        public SessionMode Mode=>session.Mode;
        public LifecycleOperation Operation=>operation;
        public int RequestCount {get;private set;}
        public Task<LifecycleReadPage> ReadNextAsync(string source,CancellationToken ct)
        {
            current();ct.ThrowIfCancellationRequested();RequestCount++;
            return Task.FromResult(new LifecycleReadPage(source=="user"?[new JsonObject
            {
                ["id"]=operation.UserId,["displayName"]="Native synthetic target",["userPrincipalName"]="native-target@example.invalid",
                ["userType"]="Member",["accountEnabled"]=true,["onPremisesSyncEnabled"]=false
            }]:[],false));
        }
        public void Dispose(){}
    }

    private static void CheckPreparedLifecycleNativeFlows(string source,ShellViewModel original,Window window,FrameworkElement content,string output)
    {
        // Own a separate synthetic evidence root: existing UI uncertainty fixtures must not be removed to get a preview.
        var root=Path.Combine(output,"lifecycle-native-"+Guid.NewGuid().ToString("N"));
        CopyFolder(Path.Combine(source,"standards"),Path.Combine(root,"standards"));
        StandardsManifest.Write(Path.Combine(root,"standards"),StandardsManifest.Generate(Path.Combine(root,"standards"),"Synthetic lifecycle native fixture",DateTimeOffset.UtcNow));
        var paths=ToolkitPaths.Resolve(root);paths.EnsureWritableFolders();
        using var logger=new ToolkitLogger(paths.LogsDirectory,LogLevel.Debug);
        var workspace=new Workspace(paths,new ToolkitSettings(),logger,true);workspace.Initialise();SeedWorkspace(workspace);
        var session=workspace.Session!;session.Scopes=["User.Read.All","Group.Read.All","Organization.Read.All","RoleManagement.Read.Directory","User.EnableDisableAccount.All"];
        session.TokenExpiresAt=Timestamps.Format(DateTimeOffset.UtcNow.AddMinutes(10));
        workspace.Snapshot!.CapturedAt=Timestamps.Format(DateTimeOffset.UtcNow);workspace.Evidence.SaveSnapshot(workspace.Snapshot);
        var shell=new ShellViewModel(workspace);var page=shell.Page<LifecycleViewModel>();
        var requests=0;
        Func<LifecycleOperation,Action,ILifecycleReadSource> factory=(operation,current)=>{requests++;return new NativeLifecycleSource(session,operation,current);};
        typeof(LifecycleViewModel).GetProperty("SourceFactory",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(page,factory);
        try
        {
            window.DataContext=shell;content.DataContext=shell;shell.Navigate("lifecycle");
            page.SelectedUserId=Id(999);page.SelectedActionId="disable-account";
            AwaitReader(page.CheckReadinessAsync());AwaitReader(page.PreviewAsync());
            if(!page.HasPreview||!page.BeforeDetails.Contains("Native synthetic target",StringComparison.Ordinal)||requests!=2)
                throw new InvalidOperationException("Native lifecycle readiness/preview did not retain the owned original before-values.");
            workspace.SetExperimentalChanges(true);
            foreach(var size in PageSizes)
            {
                FitNativeReportClientSize(window,content,size);var tabs=Descendants(content).OfType<TabControl>().Single(t=>t.Items.OfType<TabItem>().Any(i=>Equals(i.Header,"Exact review")));
                tabs.SelectedIndex=1;content.UpdateLayout();Pump();
                var before=Descendants(content).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Captured lifecycle before-values and entitlement effects");
                if(before.Text!=page.BeforeDetails||!before.IsReadOnly)throw new InvalidOperationException("Native review lost its exact copyable before-values.");
                CaptureReportState(content,output,"lifecycle-prepared-exact-review",size,checkPageScrolling:false);
                page.ProtectedReviewed=false;page.DownstreamReviewed=false;
                var dialog=new LifecycleConfirmationDialog(page){Owner=window,ShowActivated=false};dialog.Show();Pump();
                try
                {
                    var approve=Descendants(dialog).OfType<Button>().Single(b=>Equals(b.Content,"Approve this one action"));
                    if(approve.IsEnabled)throw new InvalidOperationException("Lifecycle native confirmation skipped its explicit acknowledgements.");
                    foreach(var name in new[]{"Confirm protected-account review","Confirm downstream ownership review"})
                        Descendants(dialog).OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)==name).IsChecked=true;
                    dialog.UpdateLayout();Pump();
                    if(!approve.IsEnabled||approve.IsDefault||!page.CanApply)throw new InvalidOperationException("Lifecycle native acknowledgement binding or deliberate approval gate is broken.");
                    var capturedBefore=Descendants(dialog).OfType<TextBox>().Single(t=>AutomationProperties.GetName(t)=="Confirmation captured lifecycle before-values and entitlement effects");
                    if(capturedBefore.Text!=page.BeforeDetails)throw new InvalidOperationException("Final confirmation lost the captured entitlement review.");
                    SaveImage(dialog,new Size(dialog.ActualWidth,dialog.ActualHeight),Path.Combine(output,$"lifecycle-prepared-confirmation-{size.Width}x{size.Height}.png"));
                }
                finally{dialog.Close();}
            }
            page.SelectedUserId=Id(998);page.SelectedUserId=Id(999);
            if(page.HasPreview||page.CanApply||page.BeforeDetails.Length!=0)throw new InvalidOperationException("A-B-A lifecycle target changes retained a reviewed preview.");
            if(Directory.Exists(Path.Combine(workspace.Evidence.TenantDirectory(Tenant),"lifecycle"))&&Directory.GetFiles(Path.Combine(workspace.Evidence.TenantDirectory(Tenant),"lifecycle"),"intent-*.json").Length!=0)
                throw new InvalidOperationException("Native preview/confirmation checks created an execution intent.");
            Console.WriteLine("Lifecycle native: owned synthetic readiness, preview, copyable before-values, three-size confirmation acknowledgements and A-B-A invalidation; no dispatch or tenant calls.");
        }
        finally{window.DataContext=original;content.DataContext=original;FitNativeReportClientSize(window,content,new Size(1480,940));}
    }

    private static void CheckLifecycleNativeFlows(ShellViewModel shell, Window window, FrameworkElement content, string output)
    {
        shell.Navigate("lifecycle");
        var page = shell.Page<LifecycleViewModel>();
        foreach (var size in PageSizes)
        {
            FitNativeReportClientSize(window, content, size);
            var tabs = Descendants(content).OfType<TabControl>().Single(t => t.Items.OfType<TabItem>().Any(i => Equals(i.Header, "Select and check")));
            tabs.SelectedIndex = 0; content.UpdateLayout(); Pump();
            var user = Descendants(content).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "Exact lifecycle target user ID");
            // A GUID absent from cached choices must survive the actual WPF ComboBox/TextBox binding cycle.
            user.Text = Id(999); content.UpdateLayout(); Pump();
            if (page.SelectedUserId != Id(999)) throw new InvalidOperationException("Lifecycle manual user ID was lost to an empty captured picker.");
            user.BringIntoView(); content.UpdateLayout(); Pump();
            if (!user.Focus() || !user.IsTabStop) throw new InvalidOperationException("Lifecycle manual target is not keyboard reachable.");
            if (page.CanApply || page.CanReviewAction || string.IsNullOrWhiteSpace(page.ApplyGuidance))
                throw new InvalidOperationException("Lifecycle idle navigation granted action authority or omitted the next step.");
            CaptureReportState(content, output, "lifecycle-select-manual-target", size, checkPageScrolling: false);
            tabs.SelectedIndex = 1; content.UpdateLayout(); Pump();
            CaptureReportState(content, output, "lifecycle-exact-review-closed", size, checkPageScrolling: false);
            tabs.SelectedIndex = 2; AwaitReader(page.LoadHistoryAsync()); content.UpdateLayout(); Pump();
            if (page.CanApply || !page.HistoryStatus.Contains("attempts", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Lifecycle local history lost its non-authorising meaning.");
            CaptureReportState(content, output, "lifecycle-local-handover", size, checkPageScrolling: false);
        }
        var dialog = new LifecycleConfirmationDialog(page) { Owner = window, ShowActivated = false };
        dialog.Show(); Pump(); dialog.UpdateLayout();
        try
        {
            var buttons = Descendants(dialog).OfType<Button>().ToArray();
            if (!buttons.Single(b => Equals(b.Content, "Cancel")).IsDefault
                || buttons.Single(b => Equals(b.Content, "Approve this one action")).IsEnabled)
                throw new InvalidOperationException("Lifecycle confirmation default or absent-preview refusal is unsafe.");
            SaveImage(dialog, new Size(dialog.ActualWidth, dialog.ActualHeight), Path.Combine(output, "lifecycle-confirmation-no-authority.png"));
        }
        finally { dialog.Close(); }
        page.SelectedUserId = "";
    }
}
