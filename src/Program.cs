using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly ContextMenuStrip trayMenu;
        private readonly ContextMenuStrip settingsActions;
        private readonly HotkeyHook hook;
        private ShotSettings settings = ShotSettings.Load(ShotSettings.FilePath);
        private SettingsWindow settingsWindow;
        private Overlay overlay;
        private bool updating;

        public TrayContext()
        {
            dispatcher.CreateControl();
            trayMenu = CreateMenu(true);
            settingsActions = CreateMenu(false);
            tray = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath), ContextMenuStrip = trayMenu, Visible = true
            };
            UpdateTrayText();
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) Capture();
            };
            hook = new HotkeyHook(dispatcher, settings.CaptureKey, Capture);
            if (hook.Error != 0)
                Report("Глобальная клавиша недоступна. Снимок можно сделать двойным щелчком по значку в трее. " +
                       new Win32Exception(hook.Error).Message);
        }

        private ContextMenuStrip CreateMenu(bool includeSettings)
        {
            var menu = new ContextMenuStrip { RenderMode = ToolStripRenderMode.System };
            menu.Items.Add("Сделать снимок", null, delegate { Capture(); });
            menu.Items.Add("Открыть папку снимков", null, delegate { OpenPictures(); });
            var updateItem = menu.Items.Add("Проверить обновления");
            updateItem.Click += async delegate { await CheckForUpdates(); };
            if (includeSettings) menu.Items.Add("Настройки", null, delegate { ShowSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { ExitThread(); });
            menu.Opening += delegate
            {
                updateItem.Enabled = !updating;
                ApplyMenuAppearance(menu, WindowsAppearance.Read());
            };
            return menu;
        }

        private async Task CheckForUpdates()
        {
            if (updating) return;
            if (overlay != null)
            {
                Report("Завершите работу со снимком перед обновлением.");
                return;
            }
            updating = true;
            bool restart = false;
            try
            {
                restart = await Updater.CheckAndPrepareAsync();
            }
            catch (Exception error)
            {
                MessageBox.Show("Не удалось обновить GrayShotV2. " + error.Message,
                    "GrayShotV2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { updating = false; }
            if (restart) ExitThread();
        }

        private void UpdateTrayText()
        {
            tray.Text = "GrayShotV2 · " + Hotkeys.Label(settings.CaptureKey);
        }

        private void ShowSettings()
        {
            if (overlay != null || updating) return;
            try
            {
                if (settingsWindow == null)
                {
                    settingsWindow = new SettingsWindow(settings, hook, settingsActions, SaveSettings) { Icon = tray.Icon };
                    settingsWindow.FormClosed += delegate { settingsWindow = null; hook.Paused = false; };
                    hook.Paused = true;
                }
                if (!settingsWindow.Visible) settingsWindow.Show();
                if (settingsWindow.WindowState == FormWindowState.Minimized)
                    settingsWindow.WindowState = FormWindowState.Normal;
                settingsWindow.Activate();
            }
            catch (Exception error)
            {
                if (settingsWindow != null) settingsWindow.Dispose();
                settingsWindow = null;
                hook.Paused = false;
                Report("Не удалось открыть настройки. " + error.Message);
            }
        }

        private void SaveSettings(ShotSettings changed)
        {
            changed.Save(ShotSettings.FilePath);
            settings = changed;
            hook.Shortcut = settings.CaptureKey;
            UpdateTrayText();
        }

        private static void ApplyMenuAppearance(ContextMenuStrip menu, WindowsAppearance appearance)
        {
            if (appearance.Dark)
                menu.Renderer = new DarkTrayMenuRenderer(appearance);
            else
                menu.RenderMode = ToolStripRenderMode.System;
            menu.Opacity = appearance.Transparency ? 0.95 : 1;
            menu.AllowTransparency = appearance.Transparency;
        }

        private void Capture()
        {
            if (overlay != null || updating) return;
            bool restoreSettings = settingsWindow != null && settingsWindow.Visible;
            if (restoreSettings) settingsWindow.Hide();
            Bitmap desktop = null;
            try
            {
                var bounds = SystemInformation.VirtualScreen;
                desktop = DesktopCapture.Take(bounds);
                overlay = new Overlay(desktop, bounds, settings);
                desktop = null;
                overlay.FormClosed += delegate
                {
                    overlay = null;
                    if (restoreSettings && settingsWindow != null) ShowSettings();
                };
                overlay.Show();
                overlay.Activate();
            }
            catch (Exception error)
            {
                if (desktop != null) desktop.Dispose();
                if (overlay != null) overlay.Dispose();
                overlay = null;
                if (restoreSettings && settingsWindow != null) ShowSettings();
                Report("Не удалось открыть снимок. " + error.Message);
            }
        }

        private void OpenPictures()
        {
            try
            {
                string path = settings.PicturesRoot();
                Directory.CreateDirectory(path);
                using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
            }
            catch (Exception error) { Report(error.Message); }
        }

        private void Report(string message)
        {
            tray.ShowBalloonTip(5000, "GrayShotV2", message, ToolTipIcon.Warning);
        }

        protected override void ExitThreadCore()
        {
            if (updating || (overlay != null && overlay.Busy)) return;
            if (settingsWindow != null) settingsWindow.Close();
            if (overlay != null) overlay.Close();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (settingsWindow != null) settingsWindow.Close();
                hook.Dispose();
                var icon = tray.Icon;
                tray.Dispose();
                if (icon != null) icon.Dispose();
                trayMenu.Dispose();
                settingsActions.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class DarkTrayMenuRenderer : ToolStripSystemRenderer
    {
        private readonly WindowsAppearance appearance;

        public DarkTrayMenuRenderer(WindowsAppearance appearance)
        {
            this.appearance = appearance;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var fill = new SolidBrush(appearance.Surface))
                e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(appearance.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected || e.Item.Pressed)
            {
                using (var fill = new SolidBrush(appearance.Inset))
                    e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? appearance.Text : Color.FromArgb(160, 160, 160);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(appearance.Border))
                e.Graphics.DrawLine(pen, 6, e.Item.Height / 2, e.Item.Width - 7, e.Item.Height / 2);
        }
    }
}
