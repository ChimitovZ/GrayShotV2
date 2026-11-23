using System;
using System.Drawing;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace GrayShotV2
{
    internal sealed class WindowsAppearance
    {
        private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public readonly bool Dark, Transparency, HighContrast;
        public readonly Color Surface, Inset, Text, Border;
        public Color SurfaceTint { get { return Transparency ? Color.FromArgb(Dark ? 216 : 208, Surface) : Surface; } }
        public Color InsetTint { get { return Transparency ? Color.FromArgb(56, Inset) : Inset; } }

        internal WindowsAppearance(bool dark, bool transparency, bool highContrast)
        {
            HighContrast = highContrast;
            Dark = dark && !highContrast;
            Transparency = transparency && !highContrast;
            Inset = Dark ? Color.FromArgb(52, 52, 52) : SystemColors.Window;
            Text = Dark ? Color.WhiteSmoke : SystemColors.WindowText;
            Surface = highContrast ? Inset : Dark ? Color.FromArgb(38, 38, 38) : SystemColors.Control;
            Border = highContrast ? Text : Dark ? Color.FromArgb(105, 105, 105) : SystemColors.ControlDark;
        }

        public static WindowsAppearance Read()
        {
            if (SystemInformation.HighContrast)
                return new WindowsAppearance(false, false, true);
            try
            {
                using (RegistryKey settings = Registry.CurrentUser.OpenSubKey(Personalize))
                {
                    if (settings != null)
                        return new WindowsAppearance(
                            Equals(settings.GetValue("AppsUseLightTheme"), 0),
                            Equals(settings.GetValue("EnableTransparency"), 1), false);
                }
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            return new WindowsAppearance(false, false, false);
        }
    }
}
