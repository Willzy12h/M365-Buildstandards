using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BDIT.TenantToolkit.App.Infrastructure;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is bool b ? !b : true;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null && !(value is string s && s.Length == 0);
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Maps status words to a background brush so pass/fail/pending states are visually distinct in grids. The word is
/// always shown as well, so colour is never the only signal. Bound to finding statuses, plan actions, run and result
/// statuses, manual-check outcomes and the Deploy page's prerequisite steps.
/// </summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    // Shared by every row of every grid, so frozen: a frozen brush is immutable, cheaper to render and safe to share.
    private static readonly Brush Good = Frozen(Color.FromRgb(0xE8, 0xF5, 0xED));
    private static readonly Brush Bad = Frozen(Color.FromRgb(0xFD, 0xE7, 0xE5));
    private static readonly Brush Warn = Frozen(Color.FromRgb(0xFF, 0xF5, 0xDE));
    private static readonly Brush Info = Frozen(Color.FromRgb(0xE7, 0xF4, 0xFB));
    private static readonly Brush Neutral = Brushes.Transparent;

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        if (text is "Compliant" or "Pass" or "Verified" or "Ready" or "Observed")
            return Good;
        if (text.Contains("Missing", StringComparison.OrdinalIgnoreCase) || text is "Error" or "Fail" or "Conflict" or "Blocked" or "Drift" or "Review required" or "Interrupted" or "Rejected" || text.Contains("Removed", StringComparison.OrdinalIgnoreCase))
            return Bad;
        if (text.Contains("Partial", StringComparison.OrdinalIgnoreCase) || text.Contains("Manual", StringComparison.OrdinalIgnoreCase) || text.Contains("Unable", StringComparison.OrdinalIgnoreCase)
            || text.Contains("NotEnforced", StringComparison.OrdinalIgnoreCase) || text is "Stopped" or "Incomplete" || text.Contains("Licence", StringComparison.OrdinalIgnoreCase))
            return Warn;
        if (text is "Create" or "Update" or "Collected" or "Completed" or "Accepted" or "NoChange" or "NotApplicable" or "Deviation" or "CompliantWithDeviation" or "In progress" or "InProgress" or "Running") return Info;
        return Neutral;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Shows an enum value as words - ApprovedDeviation as "Approved deviation" - where a grid binds a model enum directly
/// and there is no domain-specific label for it.
/// </summary>
public sealed class WordsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Words(value?.ToString() ?? "");

    public static string Words(string text)
    {
        if (text.Length == 0) return text;
        var builder = new System.Text.StringBuilder(text.Length + 8);
        builder.Append(text[0]);
        for (var i = 1; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsUpper(c) && char.IsLower(text[i - 1])) builder.Append(' ').Append(char.ToLowerInvariant(c));
            else builder.Append(c);
        }
        return builder.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
