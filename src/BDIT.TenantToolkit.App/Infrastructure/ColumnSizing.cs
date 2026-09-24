using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace BDIT.TenantToolkit.App.Infrastructure;

/// <summary>
/// Stops a table squeezing its columns below the widths they were designed at.
///
/// A WPF DataGrid with a star-sized column tries to fit the viewport before it will scroll, and it does that by taking
/// width from every column down to the 20px default minimum - fixed-width columns included. The table still looks
/// complete and still scrolls, but a squeezed column cannot be read: measured at the application's minimum size, the
/// Plan page showed its Select checkboxes and its Explanation column at 20px, and a column declared 100px wide was
/// drawn at 20. Nothing indicated that content was hidden.
///
/// With this set, a column never renders narrower than its declared width, never narrower than its header, and a star
/// column keeps a readable minimum. A table that does not fit scrolls, which is the honest way to overflow. The offline
/// interface harness measures every table on every page and fails the build if a column is squeezed, so removing this
/// is caught.
/// </summary>
public static class ColumnSizing
{
    /// <summary>Matches the MinWidth the DataGridColumnHeader style gives a header; a narrower column clips its header.</summary>
    public const double HeaderMinimum = 70;

    /// <summary>A star column that declares no minimum of its own still has to show a few words.</summary>
    public const double StarMinimum = 120;

    public static readonly DependencyProperty KeepDesignedWidthsProperty = DependencyProperty.RegisterAttached(
        "KeepDesignedWidths", typeof(bool), typeof(ColumnSizing), new PropertyMetadata(false, OnKeepDesignedWidthsChanged));

    public static bool GetKeepDesignedWidths(DependencyObject element) => (bool)element.GetValue(KeepDesignedWidthsProperty);

    public static void SetKeepDesignedWidths(DependencyObject element, bool value) => element.SetValue(KeepDesignedWidthsProperty, value);

    private static void OnKeepDesignedWidthsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not DataGrid grid || e.NewValue is not true || e.OldValue is true) return;
        // Columns declared in XAML may be added before or after the style that sets this, so apply to what is there
        // now and to anything added later. Loaded is not used: it never fires for a table that is laid out but not shown.
        Apply(grid);
        ((INotifyCollectionChanged)grid.Columns).CollectionChanged += (_, _) => Apply(grid);
    }

    /// <summary>Only ever raises a minimum, so a column that already asks for more keeps it.</summary>
    public static void Apply(DataGrid grid)
    {
        foreach (var column in grid.Columns)
        {
            var width = column.Width;
            var floor = width.IsAbsolute ? Math.Max(width.Value, HeaderMinimum)
                : width.IsStar ? StarMinimum
                : HeaderMinimum;
            if (column.MinWidth < floor) column.MinWidth = floor;
        }
    }
}
