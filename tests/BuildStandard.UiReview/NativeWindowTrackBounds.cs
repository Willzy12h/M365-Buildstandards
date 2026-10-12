using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

internal static partial class Program
{
    /// <summary>Allows only the shown off-screen fixture HWND to reach its requested native client sizes.</summary>
    private sealed class NativeHarnessTrackBounds : IDisposable
    {
        private const int GetMinMaxInfoMessage = 0x0024;
        private readonly Window _window;
        private readonly HwndSource _source;
        private readonly HwndSourceHook _hook;
        private readonly double _originalWidth, _originalHeight;
        private readonly int _maximumTrackWidthPixels, _maximumTrackHeightPixels;
        private bool _disposed;

        public NativeHarnessTrackBounds(Window window, FrameworkElement content, IReadOnlyList<Size> sizes)
        {
            if (!window.IsVisible || window.ShowInTaskbar || window.Left >= 0 || window.Top >= 0
                || window.WindowState != WindowState.Normal)
                throw new InvalidOperationException("Native tracking bounds apply only to the shown, off-screen, hidden-taskbar harness window.");
            if (Marshal.SizeOf<NativeMinMaxInfo>() != 40)
                throw new InvalidOperationException("The native MINMAXINFO fixture layout must contain five 8-byte POINT values.");
            window.UpdateLayout(); content.UpdateLayout(); Pump();
            _window = window; _originalWidth = window.Width; _originalHeight = window.Height;
            _source = PresentationSource.FromVisual(window) as HwndSource
                ?? throw new InvalidOperationException("The native sizing fixture requires its shown HWND source.");
            var target = _source.CompositionTarget ?? throw new InvalidOperationException("The native sizing fixture has no device transform.");
            var chromeWidth = window.ActualWidth - content.ActualWidth;
            var chromeHeight = window.ActualHeight - content.ActualHeight;
            if (!double.IsFinite(chromeWidth) || !double.IsFinite(chromeHeight) || chromeWidth < 0 || chromeHeight < 0)
                throw new InvalidOperationException("The shown fixture's measured window chrome is invalid.");
            var maximumOuterDips = new Point(sizes.Max(s => s.Width) + chromeWidth, sizes.Max(s => s.Height) + chromeHeight);
            var maximumOuterPixels = target.TransformToDevice.Transform(maximumOuterDips);
            _maximumTrackWidthPixels = checked((int)Math.Ceiling(maximumOuterPixels.X));
            _maximumTrackHeightPixels = checked((int)Math.Ceiling(maximumOuterPixels.Y));
            if (_maximumTrackWidthPixels <= 0 || _maximumTrackHeightPixels <= 0)
                throw new InvalidOperationException("The native sizing fixture has invalid pixel bounds.");
            Console.WriteLine($"Native fixture track bound: outer {maximumOuterDips.X:0.###}x{maximumOuterDips.Y:0.###} DIP, "
                + $"{_maximumTrackWidthPixels}x{_maximumTrackHeightPixels} pixels; measured chrome {chromeWidth:0.###}x{chromeHeight:0.###} DIP.");
            _hook = AdjustTrackBounds;
            _source.AddHook(_hook);
            try
            {
                // A real resize requests WM_GETMINMAXINFO again, including if Width already held a clamped request.
                window.Width = window.ActualWidth + 1;
                window.UpdateLayout(); Pump();
            }
            catch { _source.RemoveHook(_hook); throw; }
        }

        private IntPtr AdjustTrackBounds(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == GetMinMaxInfoMessage && lParam != IntPtr.Zero)
            {
                var info = Marshal.PtrToStructure<NativeMinMaxInfo>(lParam);
                var oldWidth = info.MaxTrackSize.X; var oldHeight = info.MaxTrackSize.Y;
                info.MaxTrackSize.X = Math.Max(oldWidth, _maximumTrackWidthPixels);
                info.MaxTrackSize.Y = Math.Max(oldHeight, _maximumTrackHeightPixels);
                Marshal.StructureToPtr(info, lParam, false);
                Console.WriteLine($"Native fixture WM_GETMINMAXINFO: track {oldWidth}x{oldHeight} -> {info.MaxTrackSize.X}x{info.MaxTrackSize.Y} pixels.");
                // Keep handled false: WPF must cache this HWND's bounds and preserve its production Min/Max properties.
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (!_source.IsDisposed)
            {
                _source.RemoveHook(_hook);
                // Refresh WPF's cached native bounds without the fixture hook, then restore the prior requests.
                _window.Width = _window.ActualWidth + 1;
                _window.Width = _originalWidth; _window.Height = _originalHeight;
                _window.UpdateLayout(); Pump();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint(int x, int y)
        {
            public int X = x;
            public int Y = y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMinMaxInfo(NativePoint reserved, NativePoint maxSize, NativePoint maxPosition, NativePoint minTrackSize, NativePoint maxTrackSize)
        {
            public NativePoint Reserved = reserved;
            public NativePoint MaxSize = maxSize;
            public NativePoint MaxPosition = maxPosition;
            public NativePoint MinTrackSize = minTrackSize;
            public NativePoint MaxTrackSize = maxTrackSize;
        }
    }
}
