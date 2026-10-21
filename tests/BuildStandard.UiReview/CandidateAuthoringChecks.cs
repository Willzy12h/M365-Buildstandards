using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using BuildStandard.App.Infrastructure;
using BuildStandard.App.ViewModels;

internal static partial class Program
{
    private static void CaptureCandidateEditor(ShellViewModel shell, FrameworkElement content, Size size, string output)
    {
        var editor = shell.Page<StandardViewModel>().Authoring;
        editor.Discard(); shell.ClearErrorCommand.Execute(null);
        var panel = Descendants(content).OfType<Expander>().Single(e => e.Header?.ToString() == "Create or edit a local candidate standard — offline");
        panel.IsExpanded = true; content.UpdateLayout(); Pump();
        try
        {
            RunLocal(editor.NewDraftCommand);
            editor.SelectedControl = editor.Controls.Single(c => c.Id == "CA-001"); RunLocal(editor.LoadFieldsCommand);
            editor.SelectedField = editor.Fields.Single(f => f.Path == "/payload/displayName");
            editor.ValueText = "CA - Synthetic authoring review"; RunLocal(editor.ApplyCommand); RunLocal(editor.ValidateCommand);
            if (!editor.HasUnsavedChanges || !editor.ExportCandidateCommand.CanExecute(null)) throw new InvalidOperationException("Candidate editor did not reach validated unsaved state.");
            panel.BringIntoView(); content.UpdateLayout(); Pump();
            var label = $"candidate-authoring {(int)size.Width}x{(int)size.Height}";
            RecordUnnamedControls(content, label); RecordLowContrast(content, label); RecordClipping(content, label);
            SaveImage(content, size, Path.Combine(output, $"candidate-authoring-{(int)size.Width}x{(int)size.Height}.png"));
            // The compact viewport cannot show the entire editor at once. Capture the actual working
            // value and final actions as well as the header, rather than calling an off-screen form reviewed.
            foreach (var (name, suffix) in new[] { ("Candidate proposed setting value", "value"), ("Candidate persistent change and import review", "review"), ("Candidate operation result", "result") })
            {
                var target = Descendants(panel).OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == name);
                target.BringIntoView(); content.UpdateLayout(); Pump();
                // TextBox BringIntoView can stop at its internal scroll host. Move each containing
                // viewport explicitly, then prove that the requested control is actually in the image.
                for (DependencyObject? ancestor = VisualTreeHelper.GetParent(target); ancestor is not null && ancestor != content;
                     ancestor = VisualTreeHelper.GetParent(ancestor))
                {
                    if (ancestor is not ScrollViewer scroll) continue;
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset + target.TranslatePoint(new Point(0, 0), scroll).Y - 8);
                    content.UpdateLayout(); Pump();
                }
                var visible = VisibleBounds(target, content);
                if (visible.Width < 100 || visible.Height < Math.Min(target.ActualHeight, 48) - 1)
                    throw new InvalidOperationException("Candidate " + suffix + " is outside the captured viewport.");
                RecordUnnamedControls(content, label + " " + suffix); RecordLowContrast(content, label + " " + suffix);
                RecordClipping(content, label + " " + suffix);
                SaveImage(content, size, Path.Combine(output, $"candidate-authoring-{suffix}-{(int)size.Width}x{(int)size.Height}.png"));
            }
        }
        finally { editor.Discard(); panel.IsExpanded = false; content.UpdateLayout(); Pump(); }
        void RunLocal(ICommand command)
        {
            if (!command.CanExecute(null)) throw new InvalidOperationException("Expected local authoring action was disabled: " + editor.Guidance);
            command.Execute(null); var timer = Stopwatch.StartNew();
            while (command is AsyncCommand { IsRunning: true })
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(20)) throw new InvalidOperationException("Local candidate authoring did not finish within its native harness bound.");
                Pump(); Thread.Sleep(10);
            }
            Pump(); if (shell.HasError) throw new InvalidOperationException("Local candidate authoring failed: " + shell.ErrorMessage);
        }
    }
}
