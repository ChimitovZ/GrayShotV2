using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var instance = new Mutex(true, @"Local\GrayShotV2", out first))
            {
                if (!first) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var tray = new TrayContext()) Application.Run(tray);
            }
        }
    }

    internal sealed class TrayContext : ApplicationContext
    {
        private readonly Control dispatcher = new Control();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly PrintScreenHook hook;
        private Overlay overlay;

        public TrayContext()
        {
            dispatcher.CreateControl();
            menu.Items.Add("Сделать снимок", null, delegate { Capture(); });
            menu.Items.Add("Открыть папку снимков", null, delegate { OpenPictures(); });
            menu.Items.Add("Выход", null, delegate { ExitThread(); });
            tray = new NotifyIcon
            {
                Icon = SystemIcons.Application, Text = "GrayShotV2 · PrtScr",
                ContextMenuStrip = menu, Visible = true
            };
            tray.DoubleClick += delegate { Capture(); };
            hook = new PrintScreenHook(dispatcher, Capture);
            if (hook.Error != 0)
                Report("Клавиша PrtScr недоступна. Снимок можно сделать через меню в трее. " +
                       new Win32Exception(hook.Error).Message);
        }

        private void Capture()
        {
            if (overlay != null) return;
            Bitmap desktop = null;
            try
            {
                var bounds = SystemInformation.VirtualScreen;
                desktop = DesktopCapture.Take(bounds);
                overlay = new Overlay(desktop, bounds);
                desktop = null;
                overlay.FormClosed += delegate { overlay = null; };
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception error)
            {
                if (desktop != null) desktop.Dispose();
                if (overlay != null) overlay.Dispose();
                overlay = null;
                Report("Не удалось открыть снимок. " + error.Message);
            }
        }

        private void OpenPictures()
        {
            try
            {
                string path = ShotExport.PicturesRoot();
                Directory.CreateDirectory(path);
                using (Process.Start("explorer.exe", "\"" + path + "\"")) { }
            }
            catch (Exception error) { Report(error.Message); }
        }

        private void Report(string message)
        {
            tray.ShowBalloonTip(5000, "GrayShotV2", message, ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            if (overlay != null && overlay.Busy) return;
            if (overlay != null) overlay.Close();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hook.Dispose();
                tray.Dispose();
                menu.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
