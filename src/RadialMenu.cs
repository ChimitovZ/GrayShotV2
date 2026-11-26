using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal sealed class RadialMenu : IDisposable
    {
        private const int EdgeMargin = 15;
        private readonly Tool[] tools;
        private readonly string[] shortcuts;
        private readonly float step;
        private readonly int outer, inner, reach;
        private readonly Font font, caption;
        public Point Center { get; private set; }
        public Tool? Hover;
        public Rectangle Bounds { get { return Circle(outer + EdgeMargin); } }

        public RadialMenu(Point origin, Rectangle monitor, Tool[] tools, string[] shortcuts)
        {
            this.tools = tools;
            this.shortcuts = shortcuts;
            step = 360f / tools.Length;
            outer = Math.Min(270, (int)(Math.Min(monitor.Width, monitor.Height) * 0.195));
            int margin = outer + EdgeMargin;
            Center = new Point(Math.Max(monitor.Left + margin, Math.Min(origin.X, monitor.Right - margin)),
                               Math.Max(monitor.Top + margin, Math.Min(origin.Y, monitor.Bottom - margin)));
            inner = (int)(outer * 0.32);
            reach = outer + Math.Max(10, outer / 14);
            font = new Font("Segoe UI", Math.Max(12, Math.Min(16, outer / 13)), FontStyle.Regular, GraphicsUnit.Pixel);
            caption = new Font("Segoe UI", Math.Max(10, Math.Min(12, outer / 17)), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private Rectangle Circle(int radius)
        {
            return new Rectangle(Center.X - radius, Center.Y - radius, 2 * radius, 2 * radius);
        }

        public Tool? Hit(Point point)
        {
            double x = point.X - Center.X;
            double y = point.Y - Center.Y;
            double distance = x * x + y * y;
            if (distance < inner * inner || distance > reach * reach) return null;
            double angle = (Math.Atan2(y, x) * 180 / Math.PI + 450) % 360;
            if (Hover.HasValue)
            {
                double delta = Math.Abs(angle - Array.IndexOf(tools, Hover.Value) * step);
                if (Math.Min(delta, 360 - delta) <= step / 2 + 4) return Hover;
            }
            return tools[(int)((angle + step / 2) % 360 / step)];
        }

        public void Draw(Graphics g, Tool selected, WindowsAppearance appearance, Action<Graphics, Rectangle> drawBlur)
        {
            Rectangle shell = Circle(outer), rim = Circle(outer - 6), hub = Circle(inner);
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (!appearance.HighContrast)
            {
                Rectangle shadowBounds = Circle(outer + 2);
                shadowBounds.Offset(0, 3);
                using (var shadow = new SolidBrush(Color.FromArgb(48, Color.Black)))
                    g.FillEllipse(shadow, shadowBounds);
            }
            if (appearance.Transparency)
            {
                GraphicsState backdrop = g.Save();
                using (var path = new GraphicsPath())
                {
                    path.AddEllipse(shell);
                    g.SetClip(path, CombineMode.Intersect);
                }
                drawBlur(g, shell);
                g.Restore(backdrop);
            }
            using (var surface = new SolidBrush(appearance.SurfaceTint))
            using (var inset = new SolidBrush(appearance.InsetTint))
            using (var border = new Pen(appearance.Border))
            {
                g.FillEllipse(surface, shell);
                g.DrawEllipse(border, shell);
                g.FillEllipse(inset, hub);
                g.DrawEllipse(border, hub);

                int radius = (int)(outer * 0.66);
                for (int i = 0; i < tools.Length; i++)
                {
                    Tool tool = tools[i];
                    float angle = -90 + i * step;
                    float start = angle - step / 2;
                    bool isHovered = tool == Hover;
                    bool isActive = tool == selected;
                    using (var path = new GraphicsPath())
                    {
                        path.AddArc(rim, start + 0.9f, step - 1.8f);
                        path.AddArc(hub, start + step - 2.7f, -step + 5.4f);
                        path.CloseFigure();
                        g.FillPath(isHovered ? SystemBrushes.Highlight : inset, path);
                        g.DrawPath(isHovered || isActive ? SystemPens.Highlight : border, path);
                    }

                    Point label = AtRadius(radius, angle);
                    Color ink = isHovered ? SystemColors.HighlightText : appearance.Text;
                    DrawIcon(g, tool, label.X, label.Y - font.Height / 2 - 5, outer / 205f, ink);
                    DrawLabel(g, ToolInfo.Label(tool), font, ink,
                        new Rectangle(label.X - radius / 2, label.Y, radius, font.Height + 2));
                    DrawLabel(g, shortcuts[(int)tool], caption, ink,
                        new Rectangle(label.X - radius / 2, label.Y + font.Height, radius, caption.Height + 2));
                }
            }
            DrawLabel(g, Hover.HasValue ? ToolInfo.Label(Hover.Value) : "Выбор", font, appearance.Text,
                new Rectangle(hub.X, Center.Y - font.Height, hub.Width, font.Height));
            DrawLabel(g, Hover.HasValue ? "Отпустите ПКМ" : "Esc · отмена", caption, appearance.Text,
                new Rectangle(hub.X, Center.Y + 2, hub.Width, caption.Height + 2));
            g.Restore(state);
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
                    case Tool.Highlight:
                        g.DrawPolygon(pen, new[] { new Point(-7, 4), new Point(-3, 7), new Point(10, -4),
                            new Point(4, -10), new Point(-6, -1) });
                        g.DrawLine(pen, -11, 10, 9, 10);
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
                TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform |
                TextFormatFlags.EndEllipsis);
        }

        public void Dispose()
        {
            font.Dispose();
            caption.Dispose();
        }
    }
}
