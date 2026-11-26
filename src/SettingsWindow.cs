using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal sealed class SettingsWindow : Form
    {
        private readonly HotkeyHook hook;
        private readonly Action<ShotSettings> save;
        private readonly Button[] hotkeyButtons = new Button[ToolInfo.Count + 1];
        private readonly Keys[] keys = new Keys[ToolInfo.Count + 1];
        private readonly CheckBox[] visibleTools = new CheckBox[ToolInfo.Count];
        private readonly WindowsAppearance appearance = WindowsAppearance.Read();
        private readonly string defaultFolder;
        private readonly TextBox folder;
        private readonly TextBox pattern;
        private readonly Label preview;
        private readonly Label error;
        private readonly DateTime previewTime = DateTime.Now;
        private int recording = -1;

        public SettingsWindow(ShotSettings settings, HotkeyHook hook, ContextMenuStrip actions,
                              Action<ShotSettings> save)
        {
            this.hook = hook;
            this.save = save;
            keys[0] = settings.CaptureKey;
            settings.ToolKeys.CopyTo(keys, 1);

            Text = "Настройки GrayShotV2";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(550, 528);
            MinimumSize = new Size(490, 360);
            MaximizeBox = false;
            BackColor = appearance.Surface;
            ForeColor = appearance.Text;

            var content = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top, Padding = new Padding(16, 12, 16, 12), ColumnCount = 3
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26));
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, TabIndex = 0 };
            scroll.Controls.Add(content);
            Controls.Add(scroll);

            AddRow(content, LabelFor("Действие"), LabelFor("Горячая клавиша"), LabelFor("В круговом меню"));
            for (int i = 0; i < hotkeyButtons.Length; i++)
            {
                int index = i;
                string label = i == 0 ? "Сделать снимок" : ToolInfo.Label((Tool)(i - 1));
                Button button = hotkeyButtons[i] = MakeButton(Hotkeys.Label(keys[i]));
                button.AccessibleName = "Горячая клавиша: " + label;
                button.Click += delegate { BeginRecording(index); };
                button.Leave += delegate { if (recording == index) EndRecording(); };
                CheckBox visible = null;
                if (i > 0)
                {
                    visible = visibleTools[i - 1] = new CheckBox
                    {
                        AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 0, 0),
                        Text = "Показывать", AccessibleName = "Показывать в круговом меню: " + label,
                        Checked = Array.IndexOf(settings.HiddenTools, (Tool)(i - 1)) < 0
                    };
                }
                AddRow(content, LabelFor(label), button, visible);
            }

            var folderLabel = LabelFor("Папка снимков");
            folderLabel.Margin = new Padding(0, 13, 0, 4);
            AddWideRow(content, folderLabel);
            if (settings.Folder == null)
            {
                try { defaultFolder = ShotExport.PicturesRoot(); }
                catch (IOException) { }
            }
            folder = MakeTextBox(folderLabel.Text, settings.Folder ?? defaultFolder);
            Button browse = MakeButton("Обзор…");
            browse.Click += delegate { BrowseFolder(); };
            AddRow(content, folder, null, browse);
            content.SetColumnSpan(folder, 2);

            var patternLabel = LabelFor("Шаблон имени PNG");
            patternLabel.Margin = new Padding(0, 10, 0, 4);
            AddWideRow(content, patternLabel);
            pattern = MakeTextBox(patternLabel.Text, settings.NamePattern);
            AddWideRow(content, pattern);
            AddWideRow(content, LabelFor("Дата и время: {yyyy} {MM} {dd} {HH} {mm} {ss} {fff}"));
            preview = LabelFor("");
            preview.AutoSize = false;
            preview.AutoEllipsis = true;
            preview.Dock = DockStyle.Fill;
            preview.Height = 22;
            AddWideRow(content, preview);
            pattern.TextChanged += delegate { UpdatePreview(); };
            UpdatePreview();

            var actionsButton = MakeButton("Действия");
            actionsButton.Click += delegate { actions.Show(actionsButton, new Point(0, actionsButton.Height)); };
            var cancelButton = MakeButton("Отмена");
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.Click += delegate { Close(); };
            var saveButton = MakeButton("Сохранить");
            saveButton.Click += delegate { SaveSettings(); };
            AcceptButton = saveButton;
            CancelButton = cancelButton;

            var footer = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Bottom,
                ColumnCount = 4, RowCount = 2, Padding = new Padding(16, 0, 16, 12), TabIndex = 1
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            error = LabelFor("");
            error.MaximumSize = new Size(415, 0);
            error.AccessibleRole = AccessibleRole.Alert;
            error.Visible = false;
            footer.Controls.Add(error, 0, 0);
            footer.SetColumnSpan(error, 4);
            foreach (Button button in new[] { actionsButton, cancelButton, saveButton }) button.Width = 94;
            footer.Controls.Add(actionsButton, 0, 1);
            footer.Controls.Add(cancelButton, 2, 1);
            footer.Controls.Add(saveButton, 3, 1);
            Controls.Add(footer);
        }

        private Button MakeButton(string text)
        {
            var button = new Button { Text = text, Dock = DockStyle.Fill, Height = 28, Margin = new Padding(2) };
            if (appearance.Dark)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = appearance.Border;
                button.FlatAppearance.MouseOverBackColor = appearance.Inset;
            }
            return button;
        }

        private TextBox MakeTextBox(string label, string text)
        {
            var box = new TextBox
            {
                AccessibleName = label, Text = text, Dock = DockStyle.Fill, Margin = new Padding(2, 4, 2, 4)
            };
            if (appearance.Dark)
            {
                box.BackColor = appearance.Inset;
                box.ForeColor = appearance.Text;
            }
            return box;
        }

        private static Label LabelFor(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, UseMnemonic = false };
        }

        private static void AddRow(TableLayoutPanel panel, params Control[] cells)
        {
            int row = panel.RowCount++;
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int col = 0; col < cells.Length; col++)
                if (cells[col] != null) panel.Controls.Add(cells[col], col, row);
        }

        private static void AddWideRow(TableLayoutPanel panel, Control control)
        {
            AddRow(panel, control);
            panel.SetColumnSpan(control, panel.ColumnCount);
        }

        private void BeginRecording(int index)
        {
            if (recording == index) { EndRecording(); return; }
            EndRecording();
            error.Visible = false;
            recording = index;
            hotkeyButtons[index].Text = "Нажмите клавиши…";
            hook.Record(Handle, ReceiveShortcut);
        }

        private void EndRecording()
        {
            if (recording < 0) return;
            hook.Record(IntPtr.Zero, null);
            hotkeyButtons[recording].Text = Hotkeys.Label(keys[recording]);
            recording = -1;
        }

        private void ReceiveShortcut(Keys shortcut)
        {
            if (recording < 0) return;
            if (shortcut == Keys.Escape) { EndRecording(); return; }
            try
            {
                Hotkeys.Validate(shortcut, recording == 0);
                keys[recording] = shortcut;
                EndRecording();
                error.Visible = false;
            }
            catch (FormatException issue) { ShowError(issue.Message); }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (recording >= 0 && hook.Error != 0 && !Hotkeys.IsModifier(keyData & Keys.KeyCode) &&
                !Hotkeys.IsWindowShortcut(keyData))
            {
                ReceiveShortcut(keyData);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            EndRecording();
            base.OnDeactivate(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            EndRecording();
            base.OnFormClosed(e);
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog { Description = "Выберите папку для снимков" })
            {
                if (Directory.Exists(folder.Text)) dialog.SelectedPath = folder.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
            }
        }

        private void UpdatePreview()
        {
            try
            {
                preview.Text = "Пример: " + previewTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) +
                    "\\" + ShotSettings.FileStem(pattern.Text, previewTime) + ".png";
            }
            catch (FormatException issue) { preview.Text = issue.Message; }
        }

        private void SaveSettings()
        {
            EndRecording();
            var settings = new ShotSettings { CaptureKey = keys[0], NamePattern = pattern.Text };
            Array.Copy(keys, 1, settings.ToolKeys, 0, settings.ToolKeys.Length);
            var hidden = new List<Tool>();
            for (int i = 0; i < visibleTools.Length; i++)
                if (!visibleTools[i].Checked) hidden.Add((Tool)i);
            settings.HiddenTools = hidden.ToArray();
            try
            {
                settings.ValidateShortcuts();
                ShotSettings.FileStem(settings.NamePattern, previewTime);
                settings.Folder = folder.Text == defaultFolder ? null : ShotSettings.NormalizeFolder(folder.Text);
                if (File.Exists(settings.Folder)) throw new FormatException("Выберите папку. Указанный путь ведёт к файлу.");
                save(settings);
                Close();
            }
            catch (Exception issue)
            {
                if (!(issue is FormatException || issue is IOException || issue is UnauthorizedAccessException ||
                      issue is ArgumentException || issue is NotSupportedException)) throw;
                ShowError(issue.Message);
            }
        }

        private void ShowError(string message)
        {
            error.Text = message;
            error.Visible = true;
        }
    }
}
