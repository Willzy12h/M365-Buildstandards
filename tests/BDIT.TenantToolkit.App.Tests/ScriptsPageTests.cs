using System.Text;
using System.Windows.Data;
using BDIT.TenantToolkit.App.Services;
using BDIT.TenantToolkit.App.ViewModels;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Configuration;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Scripts;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The Scripts &amp; Reports page: the library is found by area and search, each form is the manifest's, the engine's
/// checks gate Copy, the tenant is always shown, and nothing is produced without a confirmation of exactly what was shown.
/// </summary>
public sealed class ScriptsPageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);
    private readonly TempRoot _root = new();
    private readonly ToolkitLogger _logger;
    private readonly ShellViewModel _shell;
    private readonly ScriptsViewModel _page;
    private readonly FakePrompts _prompts = new();

    public ScriptsPageTests()
    {
        _root.WriteStandard("test.json", TestData.StandardJson);
        _root.WriteManifest();
        _logger = new ToolkitLogger(_root.Paths.LogsDirectory, LogLevel.Debug);
        var workspace = new Workspace(_root.Paths, new ToolkitSettings(), _logger, diagnostics: true);
        workspace.Initialise();
        if (workspace.Standard is null) throw new InvalidOperationException("The synthetic standard did not load: " + workspace.StandardError);
        _shell = new ShellViewModel(workspace);
        _page = _shell.Page<ScriptsViewModel>();
        _page.Now = () => Now;
        _page.Prompts = _prompts;
    }

    public void Dispose()
    {
        _logger.Dispose();
        _root.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SelectClient() => _shell.Workspace.ApplyProfileToSession(TestData.Profile(), save: false);

    private void Open(string id) => _page.Selected = _page.Items.Single(i => i.Id == id);

    /// <summary>A complete message trace: the two required dates and one of the "at least one" fields.</summary>
    private void FillMessageTrace()
    {
        Open("exo.message-trace");
        _page.Field("StartDate").Value = "2026-10-06";
        _page.Field("EndDate").Value = "2026-10-08";
        _page.Field("RecipientAddress").Value = "alex@example.com";
    }

    private ScriptArgument? Argument(string name) => _page.Binding?.Arguments.SingleOrDefault(a => a.Name == name);

    [Fact]
    public void The_page_is_in_the_navigation_and_lists_the_whole_library_grouped_by_area()
    {
        Assert.Contains(_shell.NavItems, n => n.Key == "scripts" && n.Title == "Scripts & Reports");
        Assert.Equal(ScriptCatalogue.Shipped.Entries.Count, _page.Items.Count);
        var groups = _page.ItemsView.Groups.Cast<CollectionViewGroup>().ToList();
        Assert.Equal(_page.Items.Select(i => i.Area).Distinct().OrderBy(a => a, StringComparer.CurrentCulture), groups.Select(g => (string)g.Name));
        Assert.Equal(_page.Items.Count, groups.Sum(g => g.ItemCount));
        Assert.All(_page.Items, i =>
        {
            Assert.Equal("Read only", i.Badge);
            Assert.Equal("Live status: not yet tested in a tenant", i.LiveStatus);
            Assert.Contains("ExchangeOnlineManagement", i.Needs);
            Assert.Contains("role", i.Needs);
        });
    }

    [Fact]
    public void Search_uses_the_catalogue_and_keeps_an_open_form_that_still_matches()
    {
        FillMessageTrace();
        _page.SearchText = "message trace";
        Assert.Equal(ScriptCatalogue.Shipped.Search("message trace").Select(e => e.Manifest.Id).OrderBy(x => x), _page.Items.Select(i => i.Id).OrderBy(x => x));
        Assert.Contains(_page.Items, i => i.Id == "exo.message-trace");
        Assert.Equal("exo.message-trace", _page.Selected?.Id);
        Assert.Equal("alex@example.com", _page.Field("RecipientAddress").Value);

        _page.SearchText = "no library item has this word zzzz";
        Assert.Empty(_page.Items);
        Assert.Null(_page.Selected);
        Assert.Empty(_page.Fields);
        Assert.False(_page.CopyScriptCommand.CanExecute(null));

        _page.SearchText = "";
        Assert.Equal(ScriptCatalogue.Shipped.Entries.Count, _page.Items.Count);
    }

    [Fact]
    public void The_form_is_built_from_the_manifest()
    {
        Open("exo.message-trace");
        var manifest = ScriptCatalogue.Shipped.Find("exo.message-trace").Manifest;
        Assert.Equal(manifest.Parameters.Select(p => p.Name), _page.Fields.Select(f => f.Name));
        Assert.Equal(ScriptFieldKind.Date, _page.Field("StartDate").Kind);
        Assert.Equal("From (UTC) (required)", _page.Field("StartDate").LabelText);
        Assert.Equal(ScriptFieldKind.List, _page.Field("SenderAddress").Kind);
        Assert.Contains("One per line, comma or semicolon", _page.Field("SenderAddress").Hint);
        Assert.Equal(ScriptFieldKind.Choice, _page.Field("SubjectMatch").Kind);
        Assert.Equal(new[] { "", "Contains", "StartsWith", "EndsWith" }, _page.Field("SubjectMatch").Choices.Select(c => c.Value));
        Assert.Equal(ScriptFieldKind.Choices, _page.Field("Status").Kind);
        Assert.Equal(ScriptFieldKind.Text, _page.Field("MaxRows").Kind);
        Assert.Contains("1000 is used if left blank", _page.Field("MaxRows").Hint);

        Open("exo.mailbox-permissions");
        Assert.Equal(ScriptFieldKind.Flag, _page.Field("FullAccess").Kind);
        Assert.True(_page.Field("FullAccess").IsChecked);
    }

    [Fact]
    public void Copy_stays_off_until_every_required_field_and_rule_is_satisfied()
    {
        SelectClient();
        Open("exo.message-trace");
        Assert.False(_page.IsValid);
        Assert.False(_page.CanCopy);
        Assert.False(_page.CopyScriptCommand.CanExecute(null));
        Assert.False(_page.SaveScriptCommand.CanExecute(null));
        Assert.Contains("From (UTC) is required.", _page.Problems);
        Assert.Equal("From (UTC) is required.", _page.Field("StartDate").Problem);
        Assert.Contains(_page.Problems, p => p.StartsWith("Enter at least one of", StringComparison.Ordinal));
        Assert.Throws<ToolkitException>(() => _page.Review(ScriptCopyAction.Clipboard));

        _page.Field("StartDate").Value = "2026-10-06";
        _page.Field("EndDate").Value = "8 October";
        Assert.Equal("To (UTC): enter a date as YYYY-MM-DD.", _page.Field("EndDate").Problem);
        Assert.False(_page.CanCopy);

        _page.Field("EndDate").Value = "2026-10-08";
        Assert.False(_page.CanCopy);
        _page.Field("RecipientAddress").Value = "alex@example.com";
        Assert.True(_page.IsValid, string.Join(" ", _page.Problems));
        Assert.Empty(_page.Problems);
        Assert.All(_page.Fields, f => Assert.False(f.HasProblem));
        Assert.True(_page.CopyScriptCommand.CanExecute(null));
        Assert.True(_page.SaveScriptCommand.CanExecute(null));
    }

    [Fact]
    public void Blank_optional_fields_are_left_out_of_the_command()
    {
        SelectClient();
        FillMessageTrace();
        _page.Field("Subject").Value = "   ";
        Assert.True(_page.IsValid);
        Assert.Null(Argument("Subject"));
        Assert.Null(Argument("SenderAddress"));
        Assert.Null(Argument("FromIP"));
        Assert.Null(Argument("Status"));
        Assert.Equal(new ScriptTextList(new[] { "alex@example.com" }).Values, ((ScriptTextList)Argument("RecipientAddress")!.Value).Values);
        Assert.DoesNotContain("Subject =", _page.Preview);
        Assert.DoesNotContain("SenderAddress", _page.Preview);
        Assert.Contains("RecipientAddress = @('alex@example.com')", _page.Preview);
        Assert.Contains("Read only. Makes no changes.", _page.Preview);
    }

    [Fact]
    public void A_multi_select_pick_list_binds_every_ticked_value()
    {
        SelectClient();
        FillMessageTrace();
        foreach (var choice in _page.Field("Status").Choices) choice.IsSelected = choice.Value is "Delivered" or "Quarantined";
        var status = Assert.IsType<ScriptTextList>(Argument("Status")!.Value);
        Assert.Equal(new[] { "Delivered", "Quarantined" }, status.Values);
        Assert.Contains("Status = @('Delivered', 'Quarantined')", _page.Preview);

        _page.Field("SubjectMatch").SelectedChoice = _page.Field("SubjectMatch").Choices.Single(c => c.Value == "StartsWith");
        _page.Field("Subject").Value = "Invoice";
        Assert.Equal("StartsWith", Assert.IsType<ScriptText>(Argument("SubjectMatch")!.Value).Value);
    }

    [Fact]
    public void An_unticked_box_that_defaults_to_ticked_is_left_out()
    {
        SelectClient();
        Open("exo.mailbox-permissions");
        Assert.True(_page.IsValid, string.Join(" ", _page.Problems));
        Assert.NotNull(Argument("SendAs"));
        _page.Field("SendAs").IsChecked = false;
        Assert.Null(Argument("SendAs"));
        Assert.NotNull(Argument("FullAccess"));
    }

    [Fact]
    public void Without_a_client_the_banner_is_neutral_and_copy_is_off()
    {
        FillMessageTrace();
        Assert.True(_page.IsValid);
        Assert.False(_page.HasTenant);
        Assert.Equal("No client selected", _page.BannerTitle);
        Assert.Equal("", _page.BannerTenantId);
        Assert.Contains("Select a client", _page.BannerNote);
        Assert.Equal(TenantColours.Neutral, _page.BannerColour);
        Assert.False(_page.CanCopy);
        Assert.False(_page.CopyScriptCommand.CanExecute(null));
        Assert.Throws<ToolkitException>(() => _page.Review(ScriptCopyAction.Clipboard));
    }

    [Fact]
    public void The_banner_names_the_selected_clients_tenant_in_its_own_readable_colour()
    {
        SelectClient();
        Assert.True(_page.HasTenant);
        Assert.Equal("Test client", _page.BannerTitle);
        Assert.Equal("Tenant ID " + TestData.TenantA, _page.BannerTenantId);
        Assert.Equal("Online scripts — copied scripts sign in to this tenant only", _page.BannerNote);
        Assert.Equal(TenantColours.For(TestData.TenantA), _page.BannerColour);
        Assert.Equal(TenantColours.For(TestData.TenantA.ToUpperInvariant()), _page.BannerColour);
        Assert.NotEqual(TenantColours.Neutral, _page.BannerColour);
        Assert.True(TenantColours.Contrast(_page.BannerColour.Foreground, _page.BannerColour.Background) >= 4.5);

        var other = TestData.Profile("22222222-2222-4222-8222-222222222222");
        other.Company = "Other client";
        _shell.Workspace.ApplyProfileToSession(other, save: false);
        Assert.Equal("Other client", _page.BannerTitle);
        Assert.Equal(TenantColours.For(other.TenantId), _page.BannerColour);
    }

    [Fact]
    public void Nothing_is_produced_until_the_tenant_and_values_are_confirmed()
    {
        SelectClient();
        FillMessageTrace();
        _prompts.Answer = false;
        _page.CopyScriptCommand.Execute(null);
        Assert.Equal(1, _prompts.Confirmations);
        Assert.Null(_prompts.Clipboard);
        Assert.Contains("Not copied", _page.Status);

        var review = _page.Review(ScriptCopyAction.Clipboard);
        Assert.Throws<ToolkitException>(() => _page.Generate(review, confirmed: false));
    }

    [Fact]
    public void The_confirmation_restates_the_tenant_item_and_values_and_goes_stale_when_they_change()
    {
        SelectClient();
        FillMessageTrace();
        var review = _page.Review(ScriptCopyAction.Clipboard);
        Assert.Equal("Test client", review.TenantName);
        Assert.Equal(TestData.TenantA, review.TenantId);
        Assert.Equal(TenantColours.For(TestData.TenantA), review.Colour);
        Assert.Equal("Message trace", review.ItemName);
        Assert.Equal("Read only. Makes no changes.", review.TypeText);
        Assert.Contains(_page.Selected!.Needs, review.Requirements);
        Assert.Contains(_page.Selected.Entry.Manifest.Prerequisites, review.Requirements);
        Assert.Contains(_page.Selected.LiveStatus, review.Requirements);
        foreach (var limitation in _page.Selected.Entry.Manifest.Limitations)
            Assert.Contains(limitation, review.Requirements);
        Assert.Contains(new ScriptReviewValue("Recipients", "alex@example.com"), review.Values);
        Assert.Contains(new ScriptReviewValue("From (UTC)", "2026-10-06"), review.Values);

        _page.Field("RecipientAddress").Value = "sam@example.com";
        Assert.Throws<ToolkitException>(() => _page.Generate(review, confirmed: true));

        var fresh = _page.Review(ScriptCopyAction.Clipboard);
        _shell.Workspace.ApplyProfileToSession(TestData.Profile("22222222-2222-4222-8222-222222222222"), save: false);
        Assert.Throws<ToolkitException>(() => _page.Generate(fresh, confirmed: true));
    }

    [Fact]
    public void A_confirmed_copy_is_the_reviewed_body_bound_to_the_selected_tenant()
    {
        SelectClient();
        FillMessageTrace();
        _page.CopyScriptCommand.Execute(null);
        Assert.Equal("", _shell.ErrorMessage);
        var script = _prompts.Clipboard ?? throw new InvalidOperationException("Nothing was put on the clipboard.");
        var entry = ScriptCatalogue.Shipped.Find("exo.message-trace");
        Assert.Contains("$expectedTenantId = '" + TestData.TenantA + "'", script);
        Assert.Contains("# Tenant:      Test client (" + TestData.TenantA + ")", script);
        Assert.Contains("# Script hash: " + entry.Manifest.ScriptSha256, script);
        Assert.Contains(entry.Script.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n'), script);
        Assert.Contains("RecipientAddress = @('alex@example.com')", script);
        // The blank optional Subject is left out of the form values; the reviewed body still mentions it.
        Assert.DoesNotContain("\n        Subject = ", script);
        // No connected session for this tenant, so no account is assumed.
        Assert.Contains("$signInAs = ''", script);
        Assert.Contains("Copied Message trace for Test client", _page.Status);
    }

    [Fact]
    public void Save_writes_a_new_utf8_file_with_a_byte_order_mark_and_never_overwrites()
    {
        SelectClient();
        FillMessageTrace();
        var folder = Path.Combine(_root.Paths.ReportsDirectory, "scripts-test");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "trace.ps1");
        _prompts.SavePath = path;
        _page.SaveScriptCommand.Execute(null);
        Assert.Equal("", _shell.ErrorMessage);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("$expectedTenantId = '" + TestData.TenantA + "'", Encoding.UTF8.GetString(bytes));

        var before = File.ReadAllBytes(path);
        Assert.Throws<ToolkitException>(() => ScriptsViewModel.SaveScript(path, "replacement"));
        Assert.Equal(before, File.ReadAllBytes(path));
        _page.SaveScriptCommand.Execute(null);
        Assert.Contains("already exists", _shell.ErrorMessage);
        Assert.Equal(before, File.ReadAllBytes(path));

        Assert.Throws<ToolkitException>(() => ScriptsViewModel.SaveScript(Path.Combine(folder, "trace.txt"), "text"));
        Assert.False(File.Exists(Path.Combine(folder, "trace.txt")));
    }

    private sealed class FakePrompts : IScriptCopyPrompts
    {
        public bool Answer { get; set; } = true;
        public int Confirmations { get; private set; }
        public string? Clipboard { get; private set; }
        public string? SavePath { get; set; }

        public bool Confirm(ScriptCopyReview review) { Confirmations++; return Answer; }
        public string? ChooseSavePath(string suggestedName) => SavePath;
        public void PutOnClipboard(string text) => Clipboard = text;
    }
}
