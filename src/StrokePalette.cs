using System.Drawing;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal static class StrokePalette
    {
        private const int ColorStart = 150, WidthStart = 316, ColorCell = 25, WidthCell = 29;
        public const int Width = 438, Height = 34;
        private static readonly Color[] Colors = { StrokeStyle.Default.Color, Color.FromArgb(255, 176, 71),
            Color.FromArgb(104, 222, 157), Color.FromArgb(97, 174, 255), Color.White, Color.Black };
        private static readonly int[] Thicknesses = { 2, 3, 5, 8 };

        private static Rectangle Cell(Rectangle area, int start, int size, int index)
        {
            return new Rectangle(area.X + start + index * size, area.Y + 3, size, area.Height - 6);
        }

        public static bool TryPick(Rectangle area, Point point, StrokeStyle current, out StrokeStyle style, out bool colorPicked)
        {
            colorPicked = false;
            for (int i = 0; i < Colors.Length; i++)
                if (Cell(area, ColorStart, ColorCell, i).Contains(point))
                {
                    style = new StrokeStyle(Colors[i], current.Width);
                    colorPicked = true;
                    return true;
                }
            for (int i = 0; i < Thicknesses.Length; i++)
                if (Cell(area, WidthStart, WidthCell, i).Contains(point))
                {
                    style = new StrokeStyle(current.Color, Thicknesses[i]);
                    return true;
                }
            style = current;
            return false;
        }

        public static void Draw(Graphics g, Rectangle area, Font font, StrokeStyle style, WindowsAppearance appearance)
        {
            using (var border = new Pen(appearance.Border))
            {
                g.DrawLine(border, area.X + ColorStart - 6, area.Y + 8,
                    area.X + ColorStart - 6, area.Bottom - 8);
                g.DrawLine(border, area.X + WidthStart - 8, area.Y + 8,
                    area.X + WidthStart - 8, area.Bottom - 8);
            }
            using (var swatchBorder = new Pen(appearance.Text))
            {
                for (int i = 0; i < Colors.Length; i++)
                {
                    Rectangle cell = Rectangle.Inflate(Cell(area, ColorStart, ColorCell, i), -2, -1);
                    if (Colors[i].ToArgb() == style.Color.ToArgb())
                        g.FillRectangle(SystemBrushes.Highlight, cell);
                    var swatch = new Rectangle(cell.X + 3, cell.Y + 5, 15, 15);
                    using (var fill = new SolidBrush(Colors[i])) g.FillRectangle(fill, swatch);
                    g.DrawRectangle(swatchBorder, swatch.X, swatch.Y, swatch.Width - 1, swatch.Height - 1);
                }
            }

            using (var inkBrush = new SolidBrush(appearance.Text))
            {
                for (int i = 0; i < Thicknesses.Length; i++)
                {
                    int thickness = Thicknesses[i];
                    Rectangle cell = Rectangle.Inflate(Cell(area, WidthStart, WidthCell, i), -2, -1);
                    bool selected = thickness == style.Width;
                    if (selected) g.FillRectangle(SystemBrushes.Highlight, cell);
                    Color ink = selected ? SystemColors.HighlightText : appearance.Text;
                    var label = new Rectangle(cell.X, cell.Y + 1, cell.Width, 13);
                    TextRenderer.DrawText(g, thickness.ToString(), font, label, ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
                    g.FillRectangle(selected ? SystemBrushes.HighlightText : inkBrush,
                        cell.X + 6, cell.Bottom - 7 - thickness / 2, cell.Width - 12, thickness);
                }
            }
        }
    }
}
