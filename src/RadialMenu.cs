using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal sealed class RadialMenu : IDisposable
    {
        private static readonly float Step = 360f / ToolInfo.Count;
        internal static readonly Color Accent = Color.FromArgb(164, 239, 218);
        private const int Shadow = 14;
        private readonly int outer, inner, reach;
        private readonly Font font, caption;
        public Point Center { get; private set; }
        public int Hover = -1;
        public Rectangle Bounds { get { return Circle(outer + Shadow + 1); } }

        public RadialMenu(Point origin, Rectangle monitor)
        {
            outer = Math.Min(270, (int)(Math.Min(monitor.Width, monitor.Height) * 0.195));
            int margin = outer + Shadow + 1;
            Center = new Point(Math.Max(monitor.Left + margin, Math.Min(origin.X, monitor.Right - margin)),
                               Math.Max(monitor.Top + margin, Math.Min(origin.Y, monitor.Bottom - margin)));
            inner = (int)(outer * 0.32);
            reach = outer + Math.Max(10, outer / 14);
            font = new Font("Segoe UI Semibold", Math.Max(12, outer / 13), FontStyle.Regular, GraphicsUnit.Pixel);
            caption = new Font("Segoe UI", Math.Max(10, outer / 17), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private Rectangle Circle(int radius)
        {
            return new Rectangle(Center.X - radius, Center.Y - radius, 2 * radius, 2 * radius);
        }

        public int Hit(Point point)
        {
            double x = point.X - Center.X;
            double y = point.Y - Center.Y;
            double distance = x * x + y * y;
            if (distance < inner * inner || distance > reach * reach) return -1;
            double angle = (Math.Atan2(y, x) * 180 / Math.PI + 450) % 360;
            if (Hover >= 0)
            {
                double delta = Math.Abs(angle - Hover * Step);
                if (Math.Min(delta, 360 - delta) <= Step / 2 + 4) return Hover;
            }
            return (int)((angle + Step / 2) % 360 / Step);
        }

        public void Draw(Graphics g, Tool selected)
        {
            Rectangle shell = Circle(outer), rim = Circle(outer - 6), hub = Circle(inner);
            using (var shadow = new SolidBrush(Color.FromArgb(16, 3, 4, 12)))
            using (var baseBrush = new SolidBrush(Color.FromArgb(244, 14, 17, 28)))
            using (var normal = new LinearGradientBrush(shell, Color.FromArgb(46, 49, 67), Color.FromArgb(28, 31, 46), 80f))
            using (var active = new SolidBrush(Color.FromArgb(48, 59, 73)))
            using (var hovered = new LinearGradientBrush(shell, Color.FromArgb(70, 109, 105), Color.FromArgb(37, 72, 77), 65f))
            using (var border = new Pen(Color.FromArgb(76, 85, 105)) { LineJoin = LineJoin.Round })
            using (var highlight = new Pen(Accent, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var indicator = new SolidBrush(Accent))
            {
                for (int spread = Shadow; spread > 0; spread -= 2) g.FillEllipse(shadow, Circle(outer + spread));
                g.FillEllipse(baseBrush, shell);
                g.DrawEllipse(border, shell);
                g.DrawEllipse(border, Circle(inner - 6));
                for (int i = 0; i < ToolInfo.Count; i++)
                {
                    Tool tool = (Tool)i;
                    float angle = -90 + i * Step;
                    float start = angle - Step / 2;
                    bool isHovered = i == Hover;
                    bool isActive = i == (int)selected;
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(rim, start + 0.9f, Step - 1.8f);
                        path.AddArc(hub, start + Step - 2.7f, -Step + 5.4f);
                        path.CloseFigure();
                        Brush fill = isHovered ? (Brush)hovered : isActive ? active : (Brush)normal;
                        g.FillPath(fill, path);
                        g.DrawPath(isHovered ? highlight : border, path);
                    }
                    int radius = (int)(outer * 0.66);
                    Point label = AtRadius(radius, angle);
                    Color ink = isHovered ? Accent : Color.FromArgb(231, 237, 250);
                    DrawIcon(g, tool, label.X, label.Y - font.Height / 2 - 5, outer / 205f, ink);
                    DrawLabel(g, ToolInfo.Label(tool), font, ink,
                        new Rectangle(label.X - radius / 2, label.Y, radius, font.Height + 2));
                    DrawLabel(g, ToolInfo.Shortcut(tool), caption, Accent,
                        new Rectangle(label.X - radius / 2, label.Y + font.Height, radius, caption.Height + 2));
                    if (isActive && !isHovered)
                    {
                        Point dot = AtRadius(outer - 13, angle);
                        g.FillEllipse(indicator, dot.X - 2, dot.Y - 2, 4, 4);
                    }
                }
                if (Hover >= 0)
                    g.DrawArc(highlight, shell, -90 + Hover * Step - Step / 2 + 7, Step - 14);
                DrawLabel(g, Hover < 0 ? "Выбор" : ToolInfo.Label((Tool)Hover), font,
                    Hover < 0 ? Color.White : Accent, new Rectangle(hub.X, Center.Y - font.Height, hub.Width, font.Height));
                DrawLabel(g, Hover < 0 ? "Esc · отмена" : "Отпустите ПКМ", caption, Color.FromArgb(178, 188, 207),
                    new Rectangle(hub.X, Center.Y + 2, hub.Width, caption.Height + 2));
            }
        }

        private Point AtRadius(int radius, float angle)
        {
            double radians = angle * Math.PI / 180;
            return new Point(Center.X + (int)(radius * Math.Cos(radians)), Center.Y + (int)(radius * Math.Sin(radians)));
        }

        private static void DrawIcon(Graphics g, Tool tool, int x, int y, float scale, Color color)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(x, y);
            g.ScaleTransform(scale, scale);
            using (var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (tool)
                {
                    case Tool.Pencil:
                        g.DrawPolygon(pen, new[] { new Point(-10, 10), new Point(-8, 4), new Point(7, -11), new Point(11, -7), new Point(-4, 8) });
                        break;
                    case Tool.Arrow:
                        g.DrawLine(pen, -10, 10, 9, -9);
                        g.DrawLines(pen, new[] { new Point(-1, -9), new Point(9, -9), new Point(9, 1) });
                        break;
                    case Tool.Frame:
                        g.DrawRectangle(pen, -12, -8, 24, 16);
                        break;
                    case Tool.Text:
                        g.DrawLine(pen, -9, -9, 9, -9);
                        g.DrawLine(pen, 0, -9, 0, 9);
                        break;
                    case Tool.Blur:
                        g.DrawEllipse(pen, -10, -10, 20, 20);
                        g.DrawLine(pen, -5, -4, 5, -4);
                        g.DrawLine(pen, -6, 0, 6, 0);
                        g.DrawLine(pen, -5, 4, 5, 4);
                        break;
                    case Tool.Undo:
                        g.DrawArc(pen, -10, -5, 20, 16, -145, 220);
                        g.DrawLines(pen, new[] { new Point(-9, -9), new Point(-10, -1), new Point(-2, -1) });
                        break;
                    case Tool.Copy:
                        g.DrawLines(pen, new[] { new Point(-11, 5), new Point(-11, -11), new Point(5, -11) });
                        g.DrawRectangle(pen, -6, -6, 16, 16);
                        break;
                }
            }
            g.Restore(state);
        }

        private static void DrawLabel(Graphics g, string text, Font face, Color color, Rectangle bounds)
        {
            TextRenderer.DrawText(g, text, face, bounds, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
        }

        public void Dispose()
        {
            font.Dispose();
            caption.Dispose();
        }
    }
}
