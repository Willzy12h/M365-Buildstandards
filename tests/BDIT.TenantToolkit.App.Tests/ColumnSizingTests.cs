using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Controls;
using BDIT.TenantToolkit.App.Infrastructure;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

/// <summary>
/// The rules a table column is held to. The offline interface harness proves them on every real table at the minimum
/// window size; these pin the rules themselves, including the one the harness cannot see - that a column added after
/// the behaviour is switched on is covered too.
/// </summary>
public class ColumnSizingTests
{
    /// <summary>WPF controls must be created on a single-threaded apartment; xUnit runs tests on the thread pool.</summary>
    private static void OnSta(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    [Fact]
    public void A_column_is_never_narrower_than_its_design_or_its_header() => OnSta(() =>
    {
        var grid = new DataGrid();
        var wide = new DataGridTextColumn { Width = new DataGridLength(100) };
        var narrow = new DataGridTextColumn { Width = new DataGridLength(60) };
        var star = new DataGridTextColumn { Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
        var starOwnMinimum = new DataGridTextColumn { Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 260 };
        var sizedToHeader = new DataGridTextColumn { Width = DataGridLength.SizeToHeader };
        foreach (var column in new[] { wide, narrow, star, starOwnMinimum, sizedToHeader }) grid.Columns.Add(column);

        ColumnSizing.SetKeepDesignedWidths(grid, true);

        // A fixed column keeps its designed width, so a squeeze cannot hide its content.
        Assert.Equal(100, wide.MinWidth);
        // One designed narrower than its header is raised to the header, or the header is clipped.
        Assert.Equal(ColumnSizing.HeaderMinimum, narrow.MinWidth);
        Assert.Equal(ColumnSizing.StarMinimum, star.MinWidth);
        // A minimum the view already asked for is only ever raised, never lowered.
        Assert.Equal(260, starOwnMinimum.MinWidth);
        Assert.Equal(ColumnSizing.HeaderMinimum, sizedToHeader.MinWidth);
    });

    /// <summary>
    /// Columns declared in XAML can arrive before or after the style that switches this on. The harness only ever sees
    /// the finished table, so it cannot tell a rule applied at the right moment from one applied by luck.
    /// </summary>
    [Fact]
    public void A_column_added_after_the_behaviour_is_switched_on_is_held_to_the_same_rules() => OnSta(() =>
    {
        var grid = new DataGrid();
        ColumnSizing.SetKeepDesignedWidths(grid, true);

        var late = new DataGridTextColumn { Width = new DataGridLength(150) };
        grid.Columns.Add(late);

        Assert.Equal(150, late.MinWidth);
    });
}
