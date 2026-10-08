using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using BDIT.TenantToolkit.App.Infrastructure;
using BDIT.TenantToolkit.App.Views;
using BDIT.TenantToolkit.Core;
using BDIT.TenantToolkit.Core.Diagnostics;
using BDIT.TenantToolkit.Core.Safety;
using BDIT.TenantToolkit.Engine.Scripts;

namespace BDIT.TenantToolkit.App.ViewModels;

/// <summary>One library item as the list shows it.</summary>
public sealed class ScriptLibraryItem
{
    public ScriptLibraryItem(ScriptEntry entry)
    {
        Entry = entry;
        var m = entry.Manifest;
        Needs = "Needs " + string.Join("; ", m.Modules.Select(x => x.Name + " " + x.MinimumVersion + " or later"))
            + (m.Roles.Count > 0 ? " · role " + string.Join(" or ", m.Roles) : "");
    }

    public ScriptEntry Entry { get; }
    public string Id => Entry.Manifest.Id;
    public string Name => Entry.Manifest.Name;
    public string Area => Entry.Manifest.Area;
    public string Description => Entry.Manifest.Description;
    public string Needs { get; }
    public bool IsReadOnly => Entry.Manifest.Mode == ScriptMode.ReadOnly;
    public string Badge => IsReadOnly ? "Read only" : "Change · copy only";
    public string LiveStatus => Entry.Manifest.LiveStatus == ScriptLiveStatus.Verified
        ? "Live status: recorded as tested in a tenant"
        : "Live status: not yet tested in a tenant";
}

/// <summary>One choice in a pick list. A multi-select list ticks several.</summary>
public sealed class ScriptChoice : ObservableObject
{
    private bool _isSelected;
    private readonly Action _changed;

    public ScriptChoice(string value, string text, Action changed)
    {
        Value = value;
        Text = text;
        _changed = changed;
    }

    public string Value { get; }
    public string Text { get; }
    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) _changed(); } }
    public override string ToString() => Text;
}

/// <summary>How a field is drawn. The manifest's type decides it; nothing an engineer types changes it.</summary>
public enum ScriptFieldKind { Text, Date, Flag, Choice, Choices, List }

/// <summary>
/// One form field generated from a manifest parameter. It only holds what was typed or ticked; turning that into typed
/// arguments, and every check, is <see cref="ScriptInputs.Bind"/>'s job, so the form and the CLI accept exactly the same.
/// </summary>
public sealed class ScriptFormField : ObservableObject
{
    private readonly Action _changed;
    private string _value = "";
    private bool _isChecked;
    private ScriptChoice? _selectedChoice;
    private string _problem = "";

    public ScriptFormField(ScriptParameter parameter, Action changed)
    {
        Parameter = parameter;
        _changed = changed;
        Kind = parameter.Type switch
        {
            ScriptParameterType.Boolean => ScriptFieldKind.Flag,
            ScriptParameterType.Enum => parameter.Array ? ScriptFieldKind.Choices : ScriptFieldKind.Choice,
            ScriptParameterType.Date => ScriptFieldKind.Date,
            _ => parameter.Array ? ScriptFieldKind.List : ScriptFieldKind.Text
        };
        if (Kind == ScriptFieldKind.Flag) _isChecked = string.Equals(parameter.Default, "true", StringComparison.OrdinalIgnoreCase);
        if (Kind == ScriptFieldKind.Choice)
        {
            // An optional pick list starts unset, which leaves the field out, or uses the default the manifest shows.
            var unset = parameter.Required ? "Choose one" : parameter.Default is { Length: > 0 } d ? "Default (" + d + ")" : "Not set";
            Choices.Add(new ScriptChoice("", unset, () => { }));
            foreach (var allowed in parameter.Allowed ?? Array.Empty<string>()) Choices.Add(new ScriptChoice(allowed, allowed, () => { }));
            _selectedChoice = Choices[0];
        }
        if (Kind == ScriptFieldKind.Choices)
            foreach (var allowed in parameter.Allowed ?? Array.Empty<string>()) Choices.Add(new ScriptChoice(allowed, allowed, _changed));
    }

    public ScriptParameter Parameter { get; }
    public string Name => Parameter.Name;
    public string Label => Parameter.Label;
    public string Help => Parameter.Help;
    public bool IsRequired => Parameter.Required;
    public ScriptFieldKind Kind { get; }
    public string LabelText => Label + (IsRequired ? " (required)" : "");
    public ObservableCollection<ScriptChoice> Choices { get; } = new();

    public bool ShowsLabel => Kind != ScriptFieldKind.Flag;
    public bool IsTextEntry => Kind is ScriptFieldKind.Text or ScriptFieldKind.Date;
    public bool IsList => Kind == ScriptFieldKind.List;
    public bool IsFlag => Kind == ScriptFieldKind.Flag;
    public bool IsChoice => Kind == ScriptFieldKind.Choice;
    public bool IsChoices => Kind == ScriptFieldKind.Choices;

    /// <summary>What kind of value the box takes, in words. Shown under the field.</summary>
    public string Hint
    {
        get
        {
            var p = Parameter;
            var what = Kind switch
            {
                ScriptFieldKind.Date => "A date as YYYY-MM-DD (UTC).",
                ScriptFieldKind.List => "One per line, comma or semicolon" + (p.MaxItems is { } most ? $"; up to {most}." : "."),
                ScriptFieldKind.Flag => "Tick to include.",
                ScriptFieldKind.Choice => "Choose one.",
                ScriptFieldKind.Choices => "Tick any that apply.",
                _ => p.Type switch
                {
                    ScriptParameterType.Integer => p.Minimum is { } min && p.Maximum is { } max ? $"A whole number from {min} to {max}." : "A whole number.",
                    ScriptParameterType.Guid => "A GUID.",
                    _ => p.Format switch
                    {
                        ScriptValueFormat.Upn => "A sign-in name such as alex@example.com.",
                        ScriptValueFormat.Smtp => "An email address.",
                        ScriptValueFormat.Mailbox => "A mailbox's email address or alias.",
                        ScriptValueFormat.Domain => "A domain such as example.com.",
                        ScriptValueFormat.IpAddress => "An IPv4 or IPv6 address.",
                        _ => "Text on one line."
                    }
                }
            };
            if (Kind == ScriptFieldKind.Flag) return what;
            return what + (IsRequired ? " Required." : p.Default is { Length: > 0 } d && Kind != ScriptFieldKind.Choice ? $" Optional; {d} is used if left blank." : " Optional; left out if blank.");
        }
    }

    public string Value { get => _value; set { if (SetProperty(ref _value, value ?? "")) _changed(); } }
    public bool IsChecked { get => _isChecked; set { if (SetProperty(ref _isChecked, value)) _changed(); } }
    public ScriptChoice? SelectedChoice { get => _selectedChoice; set { if (SetProperty(ref _selectedChoice, value)) _changed(); } }

    /// <summary>Why this field cannot be used yet, shown beside it.</summary>
    public string Problem
    {
        get => _problem;
        internal set { if (SetProperty(ref _problem, value)) OnPropertyChanged(nameof(HasProblem)); }
    }

    public bool HasProblem => Problem.Length > 0;

    /// <summary>
    /// The text handed to <see cref="ScriptInputs.Bind"/>. A tick box always says true or false, so an unticked box that
    /// defaults to ticked is really unticked; a false flag is then left out like any blank field.
    /// </summary>
    public string? Raw => Kind switch
    {
        ScriptFieldKind.Flag => IsChecked ? "true" : "false",
        ScriptFieldKind.Choice => SelectedChoice?.Value ?? "",
        ScriptFieldKind.Choices => string.Join("\n", Choices.Where(c => c.IsSelected).Select(c => c.Value)),
        _ => Value
    };

    /// <summary>Whether a binding problem is about this field, as ScriptInputs words it.</summary>
    internal bool Owns(string problem) =>
        problem.StartsWith(Label + ":", StringComparison.Ordinal)
        || problem.StartsWith(Label + " is required", StringComparison.Ordinal)
        || problem.StartsWith(Label + " takes at most", StringComparison.Ordinal);
}

/// <summary>What the confirmation is for: putting the script on the clipboard, or saving it as a file.</summary>
public enum ScriptCopyAction { Clipboard, SaveAs }

/// <summary>A filled value as the confirmation restates it.</summary>
public sealed record ScriptReviewValue(string Label, string Value);

/// <summary>
/// Everything the engineer confirms before a script is produced: the tenant (name, ID and colour), the item, that it is
/// read only, and every filled value. The fingerprint ties the confirmation to exactly this state; if the tenant or the
/// form changes before generation, the confirmation no longer applies.
/// </summary>
public sealed record ScriptCopyReview(
    ScriptCopyAction Action,
    string TenantName,
    string TenantId,
    TenantColour Colour,
    string Account,
    string ItemId,
    string ItemName,
    string TypeText,
    IReadOnlyList<ScriptReviewValue> Values,
    string Fingerprint)
{
    public string ConfirmText => Action == ScriptCopyAction.Clipboard ? "Copy script to clipboard" : "Choose where to save";
    public string AccountText => Account.Length > 0
        ? "Signs in as " + Account + ". If another account signs in, the script stops before reading anything."
        : "Signs in with the account chosen at the Microsoft prompt";
}

/// <summary>The parts of copying that need a person or the desktop: confirming, choosing a file and the clipboard.</summary>
public interface IScriptCopyPrompts
{
    bool Confirm(ScriptCopyReview review);
    string? ChooseSavePath(string suggestedName);
    void PutOnClipboard(string text);
}

/// <summary>
/// The Scripts &amp; Reports page (INT-072/INT-080, first desktop slice). It lists the reviewed library, builds each item's
/// form from its manifest, checks it live with the engine and produces the Copy script only after an explicit
/// confirmation that restates the tenant. Nothing is run: there is no Run here, by design, until the owned PowerShell
/// session in the next slice exists.
/// </summary>
public sealed class ScriptsViewModel : PageViewModel
{
    public const string BannerNoteText = "Online scripts — copied scripts sign in to this tenant only";
    public const string ReadOnlyText = "Read only. Makes no changes.";

    private static readonly Brush NeutralLine = Frozen("#DAE3ED");

    private readonly IReadOnlyList<ScriptEntry> _library;
    private string _searchText = "";
    private ScriptLibraryItem? _selected;
    private ScriptBinding? _binding;
    private string _status = "";
    private bool _building;
    private bool _searching;

    public ScriptsViewModel(ShellViewModel shell) : base(shell, "Scripts & Reports")
    {
        try { _library = ScriptCatalogue.Shipped.Entries; }
        catch (ToolkitException ex)
        {
            // A library that fails its pins is refused whole, and the page says so instead of listing part of it.
            _library = Array.Empty<ScriptEntry>();
            LibraryError = ex.Message;
        }
        ItemsView = new ListCollectionView(Items);
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ScriptLibraryItem.Area)));
        Prompts = new ScriptCopyPrompts(shell);
        CopyScriptCommand = Sync(() => Produce(ScriptCopyAction.Clipboard), () => CanCopy);
        SaveScriptCommand = Sync(() => Produce(ScriptCopyAction.SaveAs), () => CanCopy);
        ApplySearch();
    }

    public ICommand CopyScriptCommand { get; }
    public ICommand SaveScriptCommand { get; }

    /// <summary>Confirmation, file choice and clipboard. Replaced in tests and the offline harness.</summary>
    public IScriptCopyPrompts Prompts { get; set; }

    /// <summary>The time the date rules and the copied script's header use.</summary>
    public Func<DateTimeOffset> Now { get; set; } = () => SystemClock.Instance.UtcNow;

    public string LibraryError { get; } = "";
    public bool HasLibraryError => LibraryError.Length > 0;

    // ---- list and search --------------------------------------------------------------------------------------

    public ObservableCollection<ScriptLibraryItem> Items { get; } = new();

    /// <summary>The list grouped by area, in area then name order.</summary>
    public ListCollectionView ItemsView { get; }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? "")) ApplySearch(); }
    }

    public string ResultText => _library.Count == 0 ? "The library could not be loaded."
        : Items.Count == 0 ? "No library item matches. Try fewer or different words."
        : Items.Count == _library.Count ? $"{Items.Count} items" : $"{Items.Count} of {_library.Count} items match";

    private void ApplySearch()
    {
        var keep = _selected?.Id;
        var matches = _library.Count == 0 || SearchText.Trim().Length == 0 ? _library : ScriptCatalogue.Shipped.Search(SearchText);
        // While the list is rebuilt the list box reports "nothing selected"; that is not the engineer's choice.
        _searching = true;
        try
        {
            Items.Clear();
            foreach (var entry in matches.OrderBy(e => e.Manifest.Area, StringComparer.CurrentCulture).ThenBy(e => e.Manifest.Name, StringComparer.CurrentCulture))
                Items.Add(new ScriptLibraryItem(entry));
        }
        finally { _searching = false; }
        // The open form stays open while its item still matches, so a search never discards what was typed into it.
        var same = keep is null ? null : Items.FirstOrDefault(i => i.Id == keep);
        _selected = same;
        OnPropertyChanged(nameof(Selected));
        if (same is null && keep is not null) BuildForm();
        OnPropertyChanged(nameof(ResultText));
    }

    public ScriptLibraryItem? Selected
    {
        get => _selected;
        set
        {
            if (_searching || ReferenceEquals(_selected, value)) return;
            var sameItem = value is not null && _selected?.Id == value.Id;
            _selected = value;
            OnPropertyChanged();
            if (!sameItem) BuildForm();
        }
    }

    public bool HasSelection => Selected is not null;
    public string SelectedHeading => Selected is null ? "Select an item" : Selected.Name;
    public string SelectedDetail => Selected is null
        ? "Choose a script on the left to see what it needs and fill in its form."
        : Selected.Description + "\n" + Selected.Needs + "\nPowerShell " + string.Join(" or ", Selected.Entry.Manifest.SupportedRuntimes) + ". " + Selected.Entry.Manifest.Prerequisites;
    public string Limitations => Selected is null ? "" : string.Join("\n", Selected.Entry.Manifest.Limitations.Select(l => "• " + l));

    // ---- form -------------------------------------------------------------------------------------------------

    public ObservableCollection<ScriptFormField> Fields { get; } = new();
    public ObservableCollection<string> Problems { get; } = new();
    public bool HasProblems => Problems.Count > 0;
    public ScriptBinding? Binding => _binding;
    public bool IsValid => _binding?.IsValid == true;

    public ScriptFormField Field(string name) => Fields.Single(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    private void BuildForm()
    {
        _building = true;
        Fields.Clear();
        if (Selected is not null)
            foreach (var parameter in Selected.Entry.Manifest.Parameters) Fields.Add(new ScriptFormField(parameter, Revalidate));
        _building = false;
        Status = "";
        Revalidate();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedHeading));
        OnPropertyChanged(nameof(SelectedDetail));
        OnPropertyChanged(nameof(Limitations));
    }

    /// <summary>Binds the whole form with the engine after every change, so problems and the preview are always current.</summary>
    public void Revalidate()
    {
        if (_building) return;
        Problems.Clear();
        if (Selected is null) _binding = null;
        else
        {
            var values = Fields.ToDictionary(f => f.Name, f => f.Raw, StringComparer.OrdinalIgnoreCase);
            _binding = ScriptInputs.Bind(Selected.Entry.Manifest, values, Now());
            foreach (var problem in _binding.Problems) Problems.Add(problem);
            foreach (var field in Fields) field.Problem = string.Join(" ", _binding.Problems.Where(field.Owns));
        }
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CopyBlockedText));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>The arguments exactly as the copied script will carry them, or why there are none yet.</summary>
    public string Preview
    {
        get
        {
            if (Selected is null) return "Select an item to see its command.";
            if (_binding is null || !_binding.IsValid) return "The command appears here once the form is complete.";
            var b = new StringBuilder();
            b.Append("# ").Append(Selected.Name).Append(" (").Append(Selected.Id).Append(") · ").Append(ReadOnlyText).Append('\n');
            b.Append("$arguments = @{\n");
            foreach (var argument in _binding.Arguments) b.Append("    ").Append(argument.Name).Append(" = ").Append(ScriptCopy.Literal(argument.Value)).Append('\n');
            b.Append("}\n");
            b.Append("& $library @arguments   # the reviewed body, unchanged");
            return b.ToString();
        }
    }

    // ---- tenant banner ----------------------------------------------------------------------------------------

    /// <summary>The selected client's tenant, or none. Copy targets the client profile, never whatever happens to be connected.</summary>
    public bool HasTenant => Workspace.Profile is { } p && Guid.TryParse(p.TenantId, out _);
    public string BannerTitle => HasTenant ? Workspace.Profile!.Company : "No client selected";
    public string BannerTenantId => HasTenant ? "Tenant ID " + Workspace.Profile!.TenantId : "";
    public string BannerNote => HasTenant ? BannerNoteText : "Select a client on the Connect page. Copy stays off until a client is selected.";
    public TenantColour BannerColour => TenantColours.For(HasTenant ? Workspace.Profile!.TenantId : null);
    public Brush BannerBrush => Frozen(BannerColour.Background);
    public Brush BannerTextBrush => Frozen(BannerColour.Foreground);
    public Brush BannerLineBrush => HasTenant ? BannerBrush : NeutralLine;

    /// <summary>The connected account, offered only when the session is for the selected client's tenant.</summary>
    public string Account => Workspace.Session is { } s && HasTenant && TenantConfirmation.Matches(s.TenantId, Workspace.Profile!.TenantId)
        && ScriptCopy.IsAcceptableAccount(s.Account) ? s.Account.Trim() : "";

    public bool CanCopy => HasTenant && Selected is not null && IsValid;

    public string CopyBlockedText => !HasTenant ? "Select a client on the Connect page to copy a script for its tenant."
        : Selected is null ? "Select an item first."
        : !IsValid ? "Complete the form; each problem is listed above."
        : "Copy and Save ask you to confirm the tenant and values first. Nothing runs from here.";

    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    // ---- confirmation and generation --------------------------------------------------------------------------

    /// <summary>What the confirmation shows. Refuses when the tenant or the form is not ready.</summary>
    public ScriptCopyReview Review(ScriptCopyAction action)
    {
        var item = Selected ?? throw new ToolkitException("Select a library item first.");
        if (!HasTenant) throw new ToolkitException("Select a client on the Connect page first. A copied script is always for one tenant.");
        if (_binding is not { IsValid: true } binding) throw new ToolkitException("Complete the form first: " + string.Join(" ", _binding?.Problems ?? Array.Empty<string>()));
        var profile = Workspace.Profile!;
        var tenantId = Guid.Parse(profile.TenantId).ToString("D");
        var values = binding.Arguments.Select(a => new ScriptReviewValue(
            item.Entry.Manifest.Parameters.First(p => p.Name == a.Name).Label, Display(a.Value))).ToList();
        var account = Account;
        return new ScriptCopyReview(action, profile.Company, tenantId, TenantColours.For(tenantId), account, item.Id, item.Name,
            item.IsReadOnly ? ReadOnlyText : "CHANGE. Review every line before running.", values,
            Fingerprint(tenantId, profile.Company, account, item, binding));
    }

    /// <summary>
    /// Produces the Copy script for a confirmed review. Refuses without an explicit confirmation, and refuses when the
    /// tenant or any value changed after the review was shown, so what was confirmed is exactly what is produced.
    /// </summary>
    public string Generate(ScriptCopyReview review, bool confirmed)
    {
        if (!confirmed) throw new ToolkitException("Confirm the tenant and values before the script is produced. Nothing was copied.");
        var current = Review(review.Action);
        if (current.Fingerprint != review.Fingerprint)
            throw new ToolkitException("The tenant or the form changed after it was confirmed. Review it again; nothing was copied.");
        var account = current.Account.Length > 0 ? current.Account : null;
        return ScriptCopy.Generate(Selected!.Entry, _binding!, new ScriptCopyTarget(current.TenantId, current.TenantName, account), Now());
    }

    private void Produce(ScriptCopyAction action)
    {
        var review = Review(action);
        if (!Prompts.Confirm(review)) { Status = "Not copied. Nothing was produced."; return; }
        var script = Generate(review, confirmed: true);
        if (action == ScriptCopyAction.Clipboard)
        {
            Prompts.PutOnClipboard(script);
            Status = $"Copied {review.ItemName} for {review.TenantName} ({review.TenantId}) to the clipboard. Nothing was run.";
            Workspace.Logger.Info("Scripts", $"Copied library item {review.ItemId} to the clipboard for tenant {review.TenantId}. Nothing was run.");
            return;
        }
        var path = Prompts.ChooseSavePath(SuggestedFileName(review));
        if (string.IsNullOrWhiteSpace(path)) { Status = "Not saved. Nothing was produced."; return; }
        SaveScript(path, script);
        Status = $"Saved {review.ItemName} for {review.TenantName} to {path}. Nothing was run.";
        Workspace.Logger.Info("Scripts", $"Saved library item {review.ItemId} for tenant {review.TenantId} to a .ps1 file. Nothing was run.");
    }

    public static string SuggestedFileName(ScriptCopyReview review) => $"BDIT-{review.ItemId}-{review.TenantId[..8]}.ps1";

    /// <summary>
    /// Writes a copied script as UTF-8 with a byte order mark, which Windows PowerShell 5.1 needs to read anything beyond
    /// ASCII correctly. An existing file is never replaced: the engineer chooses another name.
    /// </summary>
    public static void SaveScript(string path, string script)
    {
        if (!string.Equals(Path.GetExtension(path), ".ps1", StringComparison.OrdinalIgnoreCase))
            throw new ToolkitException("Save the script with the .ps1 extension.");
        if (File.Exists(path)) throw new ToolkitException($"{Path.GetFileName(path)} already exists. Choose another name; nothing was overwritten.");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.Write(script);
        }
        catch (IOException) when (File.Exists(path))
        {
            throw new ToolkitException($"{Path.GetFileName(path)} already exists. Choose another name; nothing was overwritten.");
        }
    }

    private static string Display(ScriptValue value) => value switch
    {
        ScriptText t => t.Value,
        ScriptTextList l => string.Join(", ", l.Values),
        ScriptFlag f => f.Value ? "Yes" : "No",
        ScriptNumber n => n.Value.ToString(CultureInfo.InvariantCulture),
        _ => ""
    };

    private static string Fingerprint(string tenantId, string tenantName, string account, ScriptLibraryItem item, ScriptBinding binding) =>
        string.Join("\n", new[] { tenantId, tenantName, account, item.Id, item.Entry.ManifestSha256, item.Entry.Manifest.ScriptSha256 }
            .Concat(binding.Arguments.Select(a => a.Name + "=" + ScriptCopy.Literal(a.Value))));

    private static Brush Frozen(string colour)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
        brush.Freeze();
        return brush;
    }

    public override void Refresh()
    {
        // The client can change while the page is open; the banner, the account and Copy follow it.
        OnPropertyChanged(nameof(HasTenant));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(BannerTenantId));
        OnPropertyChanged(nameof(BannerNote));
        OnPropertyChanged(nameof(BannerColour));
        OnPropertyChanged(nameof(BannerBrush));
        OnPropertyChanged(nameof(BannerTextBrush));
        OnPropertyChanged(nameof(BannerLineBrush));
        OnPropertyChanged(nameof(Account));
        Revalidate();
    }
}
