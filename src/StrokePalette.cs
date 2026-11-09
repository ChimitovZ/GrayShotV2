using System.Drawing;
using System.Drawing.Drawing2D;
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

        public static bool TryPick(Rectangle area, Point point, StrokeStyle current, out StrokeStyle style)
        {
            for (int i = 0; i < Colors.Length; i++)
                if (Cell(area, ColorStart, ColorCell, i).Contains(point))
                {
                    style = new StrokeStyle(Colors[i], current.Width);
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

        public static void Draw(Graphics g, Rectangle area, Font font, StrokeStyle style)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var border = new Pen(Color.FromArgb(96, 107, 128)))
            using (var accent = new Pen(RadialMenu.Accent, 1.5f))
            using (var active = new SolidBrush(Color.FromArgb(52, 76, 84)))
            {
                foreach (int offset in new[] { ColorStart - 6, WidthStart - 8 })
                    g.DrawLine(border, area.X + offset, area.Y + 8, area.X + offset, area.Bottom - 8);

                for (int i = 0; i < Colors.Length; i++)
                {
                    Rectangle cell = Cell(area, ColorStart, ColorCell, i);
                    var swatch = new Rectangle(cell.X + 5, cell.Y + 7, 15, 15);
                    using (var fill = new SolidBrush(Colors[i])) g.FillEllipse(fill, swatch);
                    g.DrawEllipse(border, swatch);
                    if (Colors[i].ToArgb() == style.Color.ToArgb())
                        g.DrawEllipse(accent, Rectangle.Inflate(swatch, 3, 3));
                }

                for (int i = 0; i < Thicknesses.Length; i++)
                {
                    int thickness = Thicknesses[i];
                    Rectangle cell = Rectangle.Inflate(Cell(area, WidthStart, WidthCell, i), -2, -1);
                    bool selected = thickness == style.Width;
                    if (selected)
                    {
                        g.FillRectangle(active, cell);
                        g.DrawRectangle(accent, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
                    }
                    Color ink = selected ? RadialMenu.Accent : Color.White;
                    var label = new Rectangle(cell.X, cell.Y + 1, cell.Width, 13);
                    TextRenderer.DrawText(g, thickness.ToString(), font, label, ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping);
                    using (var line = new Pen(ink, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(line, cell.X + 6, cell.Bottom - 7, cell.Right - 7, cell.Bottom - 7);
                }
            }
            g.Restore(state);
        }
    }
}
