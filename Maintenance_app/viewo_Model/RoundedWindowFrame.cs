using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace Maintenance_app.viewo_Model
{
    // Shared chrome for auxiliary windows only. Business handlers remain in each window.
    public sealed class RoundedWindowFrame : ContentControl
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(RoundedWindowFrame), new PropertyMetadata(false, EnableWindow));
        public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
        public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);

        private Border? surface;
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            surface = GetTemplateChild("PART_Surface") as Border;
            if (surface != null)
            {
                surface.SizeChanged += (_, _) => UpdateClip();
                UpdateClip();
            }
            if (GetTemplateChild("PART_DragArea") is Border drag)
                drag.MouseLeftButtonDown += (_, e) =>
                {
                    var window = Window.GetWindow(this);
                    if (window == null) return;
                    if (e.ClickCount == 2) ToggleMaximize(window);
                    else if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
                };
            BindButton("PART_Close", w => w.Close()); // Respects the window's existing Closing event.
            BindButton("PART_Minimize", w => w.WindowState = WindowState.Minimized);
            BindButton("PART_Maximize", ToggleMaximize);
        }

        private void BindButton(string name, Action<Window> action)
        {
            if (GetTemplateChild(name) is Button button)
                button.Click += (_, _) => { var window = Window.GetWindow(this); if (window != null) action(window); };
        }
        private static void ToggleMaximize(Window window)
        {
            if (window.ResizeMode == ResizeMode.NoResize || window.ResizeMode == ResizeMode.CanMinimize) return;
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        private void UpdateClip()
        {
            if (surface != null) surface.Clip = new RectangleGeometry(new Rect(0, 0, surface.ActualWidth, surface.ActualHeight), 18, 18);
        }

        private static void EnableWindow(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            if (target is not Window window || !(bool)args.NewValue) return;
            WindowWorkArea.Attach(window);
            window.SourceInitialized += (_, _) =>
            {
                var source = (HwndSource?)PresentationSource.FromVisual(window);
                if (source == null) return;
                source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message != 0x84 || window.WindowState != WindowState.Normal ||
                        (window.ResizeMode != ResizeMode.CanResize && window.ResizeMode != ResizeMode.CanResizeWithGrip)) return IntPtr.Zero;
                    long packed = lParam.ToInt64();
                    var point = window.PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
                    int edge = window.AllowsTransparency ? 16 : 8;
                    bool left = point.X < edge, right = point.X >= window.ActualWidth - edge;
                    bool top = point.Y < edge, bottom = point.Y >= window.ActualHeight - edge;
                    int hit = top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 0;
                    if (hit == 0) return IntPtr.Zero;
                    handled = true;
                    return new IntPtr(hit);
                });
                // ReportViewer is an HWND child and cannot render inside a layered WPF window.
                // Give that one host native transparent corners instead of AllowsTransparency.
                if (!window.AllowsTransparency)
                {
                    void RoundNativeHost()
                    {
                        GetWindowRect(source.Handle, out var bounds);
                        var scale = source.CompositionTarget?.TransformToDevice.M11 ?? 1d;
                        var region = CreateRoundRectRgn(0, 0, bounds.Right - bounds.Left + 1, bounds.Bottom - bounds.Top + 1,
                            (int)(36 * scale), (int)(36 * scale));
                        if (region != IntPtr.Zero && SetWindowRgn(source.Handle, region, true) == 0) DeleteObject(region);
                    }
                    window.SizeChanged += (_, _) => RoundNativeHost();
                    RoundNativeHost();
                }
            };
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    }
}
