using System.Text;
using BDIT.TenantToolkit.App.Services;
using Xunit;

namespace BDIT.TenantToolkit.Tests;

public sealed class PortableCliHostTests
{
    [Fact]
    public void Dispatch_is_explicit_and_preserves_exact_argument_boundaries()
    {
        Assert.False(PortableCliHost.IsRequested([]));
        Assert.False(PortableCliHost.IsRequested(["--root", "--cli"]));
        var args = new[] { "--CLI", "inventory", "--snapshot", "synthetic folder/a & b.json", "--root", "synthetic root" };
        Assert.True(PortableCliHost.IsRequested(args));
        Assert.Equal(args.Skip(1), PortableCliHost.Arguments(args));
        Assert.Empty(PortableCliHost.Arguments(["--cli"]));
        Assert.Throws<ArgumentException>(() => PortableCliHost.Arguments(["inventory"]));
    }

    // Synthetic handle values. A pipe/file handle is inherited from the caller; a console handle appears only when
    // the parent console is attached, and attaching replaces every standard slot with it (the worst case).
    private static readonly IntPtr Pipe = new(0x100), ErrorFile = new(0x200), InputPipe = new(0x300), ConsoleScreen = new(0x900);
    private static readonly IntPtr Invalid = new(-1);

    [Fact]
    public void Valid_stdout_and_stderr_are_used_as_inherited_without_attaching()
    {
        var handles = new FakeHandles(Pipe, ErrorFile, InputPipe, parentConsole: true);

        Assert.Equal(CliErrorRoute.StandardError, PortableCliHost.PrepareStandardHandles(handles));
        Assert.Equal(0, handles.AttachCalls);
        Assert.Empty(handles.SetCalls);
        Assert.Equal(Pipe, handles.Get(StandardHandle.Output));
        Assert.Equal(ErrorFile, handles.Get(StandardHandle.Error));
    }

    [Fact]
    public void Valid_stdout_with_missing_stderr_attaches_and_keeps_the_inherited_stdout()
    {
        var handles = new FakeHandles(Pipe, Invalid, InputPipe, parentConsole: true);

        Assert.Equal(CliErrorRoute.StandardError, PortableCliHost.PrepareStandardHandles(handles));
        Assert.Equal(1, handles.AttachCalls);
        // The redirected pipe survives the attachment; only the missing stderr takes the console.
        Assert.Equal(Pipe, handles.Get(StandardHandle.Output));
        Assert.Equal(ConsoleScreen, handles.Get(StandardHandle.Error));
        Assert.Equal(InputPipe, handles.Get(StandardHandle.Input));
        Assert.DoesNotContain(handles.SetCalls, c => c.Kind == StandardHandle.Error);
    }

    [Fact]
    public void Valid_stdout_with_no_stderr_and_no_parent_console_runs_with_errors_on_stdout()
    {
        var handles = new FakeHandles(Pipe, IntPtr.Zero, InputPipe, parentConsole: false);

        Assert.Equal(CliErrorRoute.StandardOutput, PortableCliHost.PrepareStandardHandles(handles));
        Assert.Equal(1, handles.AttachCalls);
        Assert.Equal(Pipe, handles.Get(StandardHandle.Output));
    }

    [Fact]
    public void Missing_stdout_with_valid_stderr_attaches_and_keeps_the_inherited_stderr()
    {
        var handles = new FakeHandles(Invalid, ErrorFile, InputPipe, parentConsole: true);

        Assert.Equal(CliErrorRoute.StandardError, PortableCliHost.PrepareStandardHandles(handles));
        Assert.Equal(1, handles.AttachCalls);
        Assert.Equal(ConsoleScreen, handles.Get(StandardHandle.Output));
        Assert.Equal(ErrorFile, handles.Get(StandardHandle.Error));
        Assert.DoesNotContain(handles.SetCalls, c => c.Kind == StandardHandle.Output);
    }

    [Fact]
    public void Missing_stdout_and_stderr_use_the_parent_console_when_it_exists()
    {
        var handles = new FakeHandles(IntPtr.Zero, Invalid, InputPipe, parentConsole: true);

        Assert.Equal(CliErrorRoute.StandardError, PortableCliHost.PrepareStandardHandles(handles));
        Assert.Equal(1, handles.AttachCalls);
        Assert.Equal(ConsoleScreen, handles.Get(StandardHandle.Output));
        Assert.Equal(ConsoleScreen, handles.Get(StandardHandle.Error));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_stdout_without_a_parent_console_is_refused_whatever_stderr_is(bool stderrValid)
    {
        var handles = new FakeHandles(IntPtr.Zero, stderrValid ? ErrorFile : Invalid, InputPipe, parentConsole: false);

        Assert.Throws<IOException>(() => PortableCliHost.PrepareStandardHandles(handles));
        Assert.Equal(1, handles.AttachCalls);
    }

    [Fact]
    public void A_refused_handle_restore_is_a_failure_not_silent_loss_of_redirected_output()
    {
        var handles = new FakeHandles(Pipe, Invalid, InputPipe, parentConsole: true) { RefuseSet = true };

        Assert.Throws<System.ComponentModel.Win32Exception>(() => PortableCliHost.PrepareStandardHandles(handles));
    }

    [Fact]
    public void Errors_share_the_stdout_writer_when_stderr_is_unavailable()
    {
        using var stdout = new MemoryStream();
        var errorOpened = false;
        var (output, error) = PortableCliHost.CreateWriters(CliErrorRoute.StandardOutput, () => stdout,
            () => { errorOpened = true; return Stream.Null; });

        error.WriteLine("Refused: synthetic");
        output.WriteLine("after");
        Assert.False(errorOpened);
        Assert.Same(output, error);
        Assert.Equal("Refused: synthetic" + Environment.NewLine + "after" + Environment.NewLine, Encoding.UTF8.GetString(stdout.ToArray()));
    }

    [Fact]
    public void Errors_use_their_own_writer_when_stderr_is_available()
    {
        using var stdout = new MemoryStream();
        using var stderr = new MemoryStream();
        var (output, error) = PortableCliHost.CreateWriters(CliErrorRoute.StandardError, () => stdout, () => stderr);

        error.WriteLine("Refused: synthetic");
        Assert.NotSame(output, error);
        Assert.Empty(stdout.ToArray());
        // UTF-8 without a byte-order mark, so redirected files and pipes start with the text itself.
        Assert.Equal("Refused: synthetic" + Environment.NewLine, Encoding.UTF8.GetString(stderr.ToArray()));
    }

    private sealed class FakeHandles(IntPtr output, IntPtr error, IntPtr input, bool parentConsole) : IStandardHandles
    {
        private readonly Dictionary<StandardHandle, IntPtr> _slots = new()
        {
            [StandardHandle.Output] = output,
            [StandardHandle.Error] = error,
            [StandardHandle.Input] = input,
        };

        public int AttachCalls { get; private set; }
        public List<(StandardHandle Kind, IntPtr Handle)> SetCalls { get; } = [];
        public bool RefuseSet { get; init; }

        public IntPtr Get(StandardHandle kind) => _slots[kind];

        public bool AttachParentConsole()
        {
            AttachCalls++;
            if (!parentConsole) return false;
            foreach (var kind in _slots.Keys.ToList()) _slots[kind] = ConsoleScreen;
            return true;
        }

        public bool Set(StandardHandle kind, IntPtr handle)
        {
            SetCalls.Add((kind, handle));
            if (RefuseSet) return false;
            _slots[kind] = handle;
            return true;
        }
    }
}
