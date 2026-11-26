using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal sealed class ShotSettings
    {
        public const string DefaultNamePattern = "GrayShotV2_{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}-{fff}";
        public const Keys DefaultCopyKey = Keys.Control | Keys.C;
        private static readonly Regex DateToken = new Regex(@"\{(yyyy|MM|dd|HH|mm|ss|fff)\}");
        private static readonly Regex ReservedName = new Regex(
            @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³]|CONIN\$|CONOUT\$) *(\.|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public Keys CaptureKey { get; set; }
        public Keys[] ToolKeys { get; set; }
        public Tool[] HiddenTools { get; set; }
        public string Folder { get; set; }
        public string NamePattern { get; set; }

        public ShotSettings()
        {
            CaptureKey = Keys.PrintScreen;
            ToolKeys = new[] { Keys.F, Keys.A, Keys.R, Keys.T, Keys.B, Keys.Control | Keys.Z, DefaultCopyKey };
            HiddenTools = new Tool[0];
            NamePattern = DefaultNamePattern;
        }

        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "GrayShotV2", "settings.json"); }
        }

        public string PicturesRoot() { return Folder ?? ShotExport.PicturesRoot(); }

        public Tool[] MenuTools()
        {
            var tools = new List<Tool>();
            for (int i = 0; i < ToolInfo.Count; i++)
            {
                Tool tool = (Tool)i;
                if (Array.IndexOf(HiddenTools, tool) < 0) tools.Add(tool);
            }
            return tools.ToArray();
        }

        public static string FileStem(string pattern, DateTime time)
        {
            string name = DateToken.Replace(pattern, match => time.ToString(match.Groups[1].Value,
                CultureInfo.InvariantCulture));
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            if (name.IndexOfAny(new[] { '{', '}' }) >= 0)
                throw new FormatException("Используйте только указанные обозначения даты и времени.");
            if (name.Length == 0 || name.Length > 240 || name[name.Length - 1] == '.' || name[name.Length - 1] == ' ' ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedName.IsMatch(name))
                throw new FormatException("Укажите допустимое в Windows имя файла длиной от 1 до 240 символов.");
            return name;
        }

        public static string NormalizeFolder(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            string root = Path.GetPathRoot(value);
            if (string.IsNullOrEmpty(root) || root.Length <= 2)
                throw new FormatException("Укажите полный путь к папке, например C:\\Снимки.");
            return Path.GetFullPath(value);
        }

        public void ValidateShortcuts()
        {
            Hotkeys.Validate(CaptureKey, true);
            if (ToolKeys == null || ToolKeys.Length != ToolInfo.Count || HiddenTools == null)
                throw new FormatException("Некорректный список инструментов.");
            var used = new Dictionary<Keys, string> { { CaptureKey, "Сделать снимок" } };
            for (int i = 0; i < ToolKeys.Length; i++)
            {
                Keys key = ToolKeys[i];
                Hotkeys.Validate(key, false);
                string label = ToolInfo.Label((Tool)i), other;
                if (used.TryGetValue(key, out other))
                    throw new FormatException(string.Format("{0} уже назначено на «{1}». Измените сочетание для «{2}».",
                        Hotkeys.Label(key), other, label));
                used.Add(key, label);
            }
        }

        public static ShotSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new ShotSettings();
                var settings = new JavaScriptSerializer().Deserialize<ShotSettings>(File.ReadAllText(path));
                if (settings == null || settings.NamePattern == null) return new ShotSettings();
                settings.ValidateShortcuts();
                settings.Folder = NormalizeFolder(settings.Folder);
                FileStem(settings.NamePattern, DateTime.Now);
                return settings;
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                      error is InvalidOperationException || error is FormatException || error is NotSupportedException))
                    throw;
                return new ShotSettings();
            }
        }

        public void Save(string path)
        {
            string folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);
            string temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(this));
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { File.Delete(temp); }
        }
    }

    internal static class Hotkeys
    {
        public static bool IsModifier(Keys key)
        {
            return key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu ||
                key == Keys.LWin || key == Keys.RWin || (key >= Keys.LShiftKey && key <= Keys.RMenu);
        }

        public static bool IsWindowShortcut(Keys shortcut)
        {
            Keys key = shortcut & Keys.KeyCode;
            return key == Keys.Tab || (key == Keys.F4 && (shortcut & Keys.Alt) != 0) ||
                (key == Keys.Escape && (shortcut & (Keys.Control | Keys.Alt)) != 0);
        }

        public static void Validate(Keys shortcut, bool global)
        {
            Keys key = shortcut & Keys.KeyCode;
            Keys modifiers = shortcut & Keys.Modifiers;
            if (key < Keys.Back || key > Keys.OemClear || IsModifier(key) ||
                (modifiers & ~(Keys.Control | Keys.Alt | Keys.Shift)) != 0)
                throw new FormatException("Добавьте к сочетанию основную клавишу.");
            if (shortcut == Keys.Escape || IsWindowShortcut(shortcut) ||
                (!global && (shortcut == Keys.Delete || (shortcut >= Keys.Left && shortcut <= Keys.Down))))
                throw new FormatException("Это сочетание используется для навигации или управления окном.");
            if (global && key != Keys.PrintScreen && (key < Keys.F1 || key > Keys.F24) &&
                (modifiers & (Keys.Control | Keys.Alt)) == 0)
                throw new FormatException("Для глобальной клавиши используйте PrtScr, F1–F24 или сочетание с Ctrl или Alt.");
        }

        public static string Label(Keys shortcut)
        {
            Keys key = shortcut & Keys.KeyCode;
            string prefix = "";
            if ((shortcut & Keys.Control) != 0) prefix += "Ctrl+";
            if ((shortcut & Keys.Alt) != 0) prefix += "Alt+";
            if ((shortcut & Keys.Shift) != 0) prefix += "Shift+";
            if (key == Keys.PrintScreen) return prefix + "PrtScr";
            return prefix + new KeysConverter().ConvertToInvariantString(key);
        }
    }
}
