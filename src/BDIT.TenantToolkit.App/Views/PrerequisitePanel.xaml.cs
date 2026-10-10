using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using BDIT.TenantToolkit.Core.Models;

namespace BDIT.TenantToolkit.App.Views;

/// <summary>Read-only prerequisite guidance shared by catalogue, planning and automation pages.</summary>
public partial class PrerequisitePanel : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items),
        typeof(IReadOnlyList<ControlPrerequisite>), typeof(PrerequisitePanel), new PropertyMetadata(null));

    public IReadOnlyList<ControlPrerequisite>? Items
    {
        get => (IReadOnlyList<ControlPrerequisite>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public PrerequisitePanel() => InitializeComponent();

    private void OpenDocumentation(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        // Catalogue guidance must never turn a documentation click into a consent URL or a local executable.
        if (!e.Uri.IsAbsoluteUri || e.Uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(e.Uri.Host, "learn.microsoft.com", StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception)
        {
            MessageBox.Show("Microsoft documentation could not be opened. Check your default browser and try again.",
                "Documentation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
