using Point = System.Windows.Point;
using Size = System.Windows.Size;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;


namespace Maintenance_app.viewo_Model
{
    internal static class WindowWorkArea
    {
        public static void Attach(Window window)
        {
            window.SourceInitialized += (sender, args) =>
            {
                var source = PresentationSource.FromVisual(window) as HwndSource;
                if (source != null) source.AddHook(Hook);
            };
        }
        private static IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x24) return IntPtr.Zero;
            var info = new MonitorInformation { Size = Marshal.SizeOf(typeof(MonitorInformation)) };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return IntPtr.Zero;
            var limits = (MinMaxInformation)Marshal.PtrToStructure(lParam, typeof(MinMaxInformation));
            limits.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            limits.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            limits.MaxSize.X = info.Work.Right - info.Work.Left;
            limits.MaxSize.Y = info.Work.Bottom - info.Work.Top;
            Marshal.StructureToPtr(limits, lParam, false);
            handled = true;
            return IntPtr.Zero;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct MinMaxInformation { public Point Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInformation { public int Size; public Rect Monitor, Work; public uint Flags; }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInformation info);
    }

}
