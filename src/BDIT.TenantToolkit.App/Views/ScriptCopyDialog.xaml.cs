using System.Windows;
using System.Windows.Media;
using BDIT.TenantToolkit.App.ViewModels;
using Microsoft.Win32;

namespace BDIT.TenantToolkit.App.Views;

/// <summary>
/// The confirmation before a library script is copied or saved: it restates the tenant with its colour band, the item,
/// that it is read only, and every value, and produces nothing until the engineer ticks the box and confirms. Generation
/// stays in <see cref="ScriptsViewModel"/>; this window only collects the decision.
/// </summary>
public partial class ScriptCopyDialog : Window
{
    public ScriptCopyDialog(ScriptCopyReview review)
    {
        InitializeComponent();
        Review = review;
        var band = Brush(review.Colour.Background);
        var text = Brush(review.Colour.Foreground);
        Band.Background = band;
        Band.BorderBrush = band;
        TenantNameText.Text = review.TenantName;
        TenantIdText.Text = "Tenant ID " + review.TenantId;
        AccountText.Text = review.AccountText;
        TenantNameText.Foreground = TenantIdText.Foreground = AccountText.Foreground = text;
        ItemText.Text = review.ItemName + " (" + review.ItemId + ")";
        TypeText.Text = review.TypeText;
        RequirementsText.Text = review.Requirements;
        foreach (var value in review.Values) ValueList.Items.Add(value.Label + ": " + value.Value);
        if (review.Values.Count == 0) ValueList.Items.Add("No values: the script runs with its own defaults.");
        ConfirmButton.Content = review.ConfirmText;
        ApprovalBox.Focus();
    }

    public ScriptCopyReview Review { get; }

    /// <summary>True only when the engineer ticked the confirmation and pressed the confirm button.</summary>
    public bool Confirmed { get; private set; }

    public static bool CanConfirm(bool approved) => approved;

    private void OnApprovalChanged(object sender, RoutedEventArgs e)
    { if (ConfirmButton is not null) ConfirmButton.IsEnabled = CanConfirm(ApprovalBox.IsChecked == true); }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (!CanConfirm(ApprovalBox.IsChecked == true)) return;
        Confirmed = true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static SolidColorBrush Brush(string colour)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colour));
        brush.Freeze();
        return brush;
    }
}

/// <summary>The desktop's confirmation dialog, Save As picker and clipboard for the Scripts &amp; Reports page.</summary>
public sealed class ScriptCopyPrompts : IScriptCopyPrompts
{
    private readonly ShellViewModel _shell;

    public ScriptCopyPrompts(ShellViewModel shell) => _shell = shell;

    public bool Confirm(ScriptCopyReview review)
    {
        var dialog = new ScriptCopyDialog(review) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true && dialog.Confirmed;
    }

    public string? ChooseSavePath(string suggestedName)
    {
        // Overwriting is refused when saving, so the picker does not offer it either.
        var dialog = new SaveFileDialog
        {
            Title = "Save the copied script", FileName = suggestedName, DefaultExt = ".ps1", AddExtension = true,
            Filter = "PowerShell script (*.ps1)|*.ps1", OverwritePrompt = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void PutOnClipboard(string text) => _shell.CopyToClipboard(text);
}
