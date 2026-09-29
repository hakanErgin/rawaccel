using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CurveEditor.Services
{
    /// <summary>
    /// Listens for raw mouse movement (after the driver has applied acceleration) so the chart can
    /// show where on the curve the mouse currently is. Same approach as grapher/Models/Mouse/MouseWatcher.cs.
    /// </summary>
    public sealed class RawMouseInput : IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const int RIDEV_INPUTSINK = 0x00000100;
        private const ushort MOUSE_MOVE_ABSOLUTE = 0x01;

        private readonly HwndSource source;
        private readonly Stopwatch stopwatch = Stopwatch.StartNew();

        public RawMouseInput(HwndSource source)
        {
            this.source = source;

            var device = new RAWINPUTDEVICE
            {
                UsagePage = 0x01,
                Usage = 0x02,
                Flags = RIDEV_INPUTSINK,
                Target = source.Handle,
            };

            if (RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
            {
                source.AddHook(Hook);
            }
        }

        /// <summary> Raised with the output movement (counts) and the time since the previous report (ms). </summary>
        public event Action<double, double, double> Moved;

        private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_INPUT) return IntPtr.Zero;

            uint size = (uint)Marshal.SizeOf(typeof(RAWINPUT));
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));

            if (GetRawInputData(lParam, RID_INPUT, out RAWINPUT input, ref size, headerSize) == uint.MaxValue)
            {
                return IntPtr.Zero;
            }

            if (input.Header.Type != 0 || (input.Mouse.Flags & MOUSE_MOVE_ABSOLUTE) != 0) return IntPtr.Zero;
            if (input.Mouse.LastX == 0 && input.Mouse.LastY == 0) return IntPtr.Zero;

            double time = stopwatch.Elapsed.TotalMilliseconds;
            stopwatch.Restart();

            // same clamps the grapher uses: at most 100 ms, at least one 8 kHz poll
            time = Math.Min(time, 100);
            time = Math.Max(time, 0.125);

            Moved?.Invoke(input.Mouse.LastX, input.Mouse.LastY, time);
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            source.RemoveHook(Hook);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort UsagePage;
            public ushort Usage;
            public int Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public int Type;
            public int Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWMOUSE
        {
            public ushort Flags;
            public uint Buttons;
            public uint RawButtons;
            public int LastX;
            public int LastY;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUT
        {
            public RAWINPUTHEADER Header;
            public RAWMOUSE Mouse;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr rawInput, uint command, out RAWINPUT data, ref uint size, uint headerSize);
    }
}
