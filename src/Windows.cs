using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal sealed class HotkeyHook : IDisposable
    {
        private readonly Control dispatcher;
        private readonly Action capture;
        private readonly Thread thread;
        private readonly HookProc callback;
        private uint threadId;
        private readonly bool[] pressed = new bool[256];
        private readonly bool[] handled = new bool[256];
        private volatile Recording recording;
        public volatile Keys Shortcut;
        public volatile bool Paused;
        public int Error { get; private set; }

        public HotkeyHook(Control dispatcher, Keys shortcut, Action capture)
        {
            this.dispatcher = dispatcher;
            this.capture = capture;
            Shortcut = shortcut;
            callback = OnKey;
            using (var ready = new ManualResetEventSlim())
            {
                thread = new Thread(() => Listen(ready)) { IsBackground = true, Name = "GrayShot hotkey" };
                thread.Start();
                ready.Wait();
            }
        }

        private void Listen(ManualResetEventSlim ready)
        {
            MessageData message;
            PeekMessage(out message, IntPtr.Zero, 0, 0, 0);
            threadId = GetCurrentThreadId();
            IntPtr hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
            Error = hook == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0;
            ready.Set();
            if (hook == IntPtr.Zero) return;
            try
            {
                while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0) { }
            }
            finally { UnhookWindowsHookEx(hook); }
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code == 0)
            {
                int key = Marshal.ReadInt32(data);
                if (key > 0 && key < pressed.Length)
                {
                    long keyMessage = message.ToInt64();
                    if (keyMessage == 0x100 || keyMessage == 0x104)
                    {
                        if (!pressed[key])
                        {
                            pressed[key] = true;
                            if (!Hotkeys.IsModifier((Keys)key)) handled[key] = HandleKey((Keys)key);
                        }
                        if (handled[key]) return new IntPtr(1);
                    }
                    else if (keyMessage == 0x101 || keyMessage == 0x105)
                    {
                        bool swallow = handled[key];
                        pressed[key] = handled[key] = false;
                        if (swallow) return new IntPtr(1);
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        private bool HandleKey(Keys key)
        {
            Recording current = recording;
            if ((Paused && current == null) || (current == null && key != (Shortcut & Keys.KeyCode))) return false;
            if (IsDown(Keys.LWin) || IsDown(Keys.RWin)) return false;
            if (IsDown(Keys.ControlKey)) key |= Keys.Control;
            if (IsDown(Keys.ShiftKey)) key |= Keys.Shift;
            if (IsDown(Keys.Menu)) key |= Keys.Alt;

            try
            {
                if (current != null && GetForegroundWindow() == current.Window)
                {
                    if (Hotkeys.IsWindowShortcut(key)) return false;
                    dispatcher.BeginInvoke((Action)delegate
                    {
                        if (recording == current) current.Receive(key);
                    });
                    return true;
                }
                if (!Paused && key == Shortcut)
                {
                    dispatcher.BeginInvoke(capture);
                    return true;
                }
            }
            catch (InvalidOperationException) { }
            return false;
        }

        public void Record(IntPtr window, Action<Keys> receive)
        {
            recording = receive == null ? null : new Recording { Window = window, Receive = receive };
        }

        private static bool IsDown(Keys key) { return GetAsyncKeyState((int)key) < 0; }

        public void Dispose()
        {
            if (thread.IsAlive)
            {
                PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero);
                thread.Join();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MessageData
        {
            public IntPtr Window;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Point;
            public uint Private;
        }

        private sealed class Recording
        {
            public IntPtr Window;
            public Action<Keys> Receive;
        }

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MessageData message, IntPtr window, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        private static extern int GetMessage(out MessageData message, IntPtr window, uint min, uint max);
        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);
    }

    internal static class DesktopCapture
    {
        public static Bitmap Take(Rectangle bounds)
        {
            IntPtr source = GetDC(IntPtr.Zero);
            if (source == IntPtr.Zero) throw new InvalidOperationException("Рабочий стол недоступен.");
            Bitmap image = null;
            try
            {
                image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
                using (var graphics = Graphics.FromImage(image))
                {
                    IntPtr target = graphics.GetHdc();
                    try
                    {
                        const uint captureCopy = 0x40cc0020; // SRCCOPY | CAPTUREBLT
                        if (!BitBlt(target, 0, 0, bounds.Width, bounds.Height, source, bounds.X, bounds.Y, captureCopy))
                            throw new Win32Exception();
                    }
                    finally { graphics.ReleaseHdc(target); }
                }
                return image;
            }
            catch
            {
                if (image != null) image.Dispose();
                throw;
            }
            finally { ReleaseDC(IntPtr.Zero, source); }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool BitBlt(IntPtr target, int x, int y, int w, int h,
                                         IntPtr source, int sx, int sy, uint rop);
    }
}
