using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GrayShotV2
{
    internal enum Tool { Pencil, Arrow, Frame, Text, Blur, Undo, Copy }

    internal static class ToolInfo
    {
        private static readonly string[] Labels = { "Карандаш", "Стрелка", "Прямоугольник", "Текст",
            "Размытие", "Отменить", "Копировать" };
        private static readonly string[] Shortcuts = { "F", "A", "R", "T", "B", "Ctrl+Z", "Ctrl+C" };
        public static int Count { get { return Labels.Length; } }
        public static string Label(Tool tool) { return Labels[(int)tool]; }
        public static string Shortcut(Tool tool) { return Shortcuts[(int)tool]; }
        public static bool SupportsStroke(Tool tool)
        {
            return tool == Tool.Pencil || tool == Tool.Arrow || tool == Tool.Frame;
        }

        public static bool TryShortcut(char key, out Tool tool)
        {
            for (int i = 0; i < Shortcuts.Length; i++)
                if (Shortcuts[i].Length == 1 && Shortcuts[i][0] == key)
                {
                    tool = (Tool)i;
                    return true;
                }
            tool = default(Tool);
            return false;
        }
    }

    internal struct StrokeStyle
    {
        public static readonly StrokeStyle Default = new StrokeStyle(Color.FromArgb(255, 68, 76), 3);
        public readonly Color Color;
        public readonly int Width;

        public StrokeStyle(Color color, int width)
        {
            Color = color;
            Width = width;
        }
    }

    internal sealed class Annotation
    {
        private static readonly Point[] NoHandles = new Point[0];
        private readonly List<Point> points;
        private Rectangle bounds;
        public readonly Tool Tool;
        public Point Start { get; private set; }
        public Point End { get; private set; }
        public string Text { get; private set; }
        public StrokeStyle Stroke = StrokeStyle.Default;
        public Rectangle Bounds { get { return bounds; } }

        public Annotation(Tool tool, Point start)
        {
            Tool = tool;
            Start = End = start;
            if (tool == Tool.Pencil) points = new List<Point> { start };
            bounds = new Rectangle(start.X, start.Y, 1, 1);
        }

        public static Rectangle Between(Point a, Point b)
        {
            return Rectangle.FromLTRB(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y),
                                      System.Math.Max(a.X, b.X) + 1, System.Math.Max(a.Y, b.Y) + 1);
        }

        public Rectangle Update(Point end)
        {
            Rectangle dirty = Tool == Tool.Pencil ? Between(End, end) : Rectangle.Union(bounds, Between(Start, end));
            if (Tool == Tool.Pencil && end != End) points.Add(end);
            End = end;
            bounds = Tool == Tool.Pencil ? Rectangle.Union(bounds, dirty) : Between(Start, end);
            dirty.Inflate(PaintMargin, PaintMargin);
            return dirty;
        }

        public void SetText(string text, Size size)
        {
            Text = text;
            bounds = new Rectangle(Start, size);
        }

        public void Move(int x, int y)
        {
            Start = new Point(Start.X + x, Start.Y + y);
            End = new Point(End.X + x, End.Y + y);
            bounds.Offset(x, y);
            if (points == null) return;
            for (int i = 0; i < points.Count; i++)
                points[i] = new Point(points[i].X + x, points[i].Y + y);
        }

        public void SetEndpoints(Point start, Point end)
        {
            Start = start;
            End = end;
            bounds = Between(start, end);
        }

        public Rectangle PaintBounds()
        {
            var area = bounds;
            area.Inflate(PaintMargin, PaintMargin);
            return area;
        }

        private int PaintMargin { get { return Tool == Tool.Arrow ? Math.Min(Stroke.Width * 5, 24) + 3 : 18; } }

        public bool Hit(Point point)
        {
            int tolerance = Math.Max(6, Stroke.Width / 2 + 4);
            int margin = Tool == Tool.Arrow ? Math.Max(11, Stroke.Width * 2 + 3) : tolerance;
            var area = Rectangle.Inflate(bounds, margin, margin);
            if (!area.Contains(point)) return false;
            switch (Tool)
            {
                case Tool.Arrow:
                    return NearSegment(point, Start, End, tolerance) ||
                           NearSegment(point, End, End, margin);
                case Tool.Frame:
                    Rectangle inside = Rectangle.Inflate(bounds, -tolerance, -tolerance);
                    return !inside.Contains(point);
                case Tool.Pencil:
                    if (points.Count == 1) return NearSegment(point, Start, Start, tolerance);
                    for (int i = 1; i < points.Count; i++)
                        if (NearSegment(point, points[i - 1], points[i], tolerance)) return true;
                    return false;
                default:
                    return true;
            }
        }

        private static bool NearSegment(Point point, Point start, Point end, int tolerance)
        {
            double x = end.X - start.X, y = end.Y - start.Y;
            double lengthSquared = x * x + y * y;
            double offsetX = point.X - start.X, offsetY = point.Y - start.Y;
            double along = lengthSquared == 0 ? 0 :
                Math.Max(0, Math.Min(1, (offsetX * x + offsetY * y) / lengthSquared));
            double dx = offsetX - along * x, dy = offsetY - along * y;
            return dx * dx + dy * dy <= tolerance * tolerance;
        }

        public Point[] Handles()
        {
            if (Tool == Tool.Arrow) return new[] { Start, End };
            if (Tool == Tool.Frame || Tool == Tool.Blur)
                return new[] { bounds.Location, new Point(bounds.Right - 1, bounds.Top),
                    new Point(bounds.Right - 1, bounds.Bottom - 1), new Point(bounds.Left, bounds.Bottom - 1) };
            return NoHandles;
        }

        public void Draw(Graphics g, Font font)
        {
            using (var brush = new SolidBrush(Stroke.Color))
            {
                if (Tool == Tool.Text)
                {
                    g.DrawString(Text, font, Brushes.Black, Start.X + 1, Start.Y + 1, StringFormat.GenericTypographic);
                    g.DrawString(Text, font, brush, Start.X, Start.Y, StringFormat.GenericTypographic);
                    return;
                }

                using (var pen = new Pen(Stroke.Color, Stroke.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    switch (Tool)
                    {
                        case Tool.Pencil:
                            if (points.Count == 1)
                                g.FillEllipse(brush, Start.X - Stroke.Width / 2f, Start.Y - Stroke.Width / 2f, Stroke.Width, Stroke.Width);
                            else g.DrawLines(pen, points.ToArray());
                            break;
                        case Tool.Arrow:
                            if (Start == End) break;
                            using (var cap = new AdjustableArrowCap(Math.Min(4f, 20f / Stroke.Width),
                                Math.Min(5f, 24f / Stroke.Width)))
                            {
                                pen.CustomEndCap = cap;
                                g.DrawLine(pen, Start, End);
                            }
                            break;
                        case Tool.Frame:
                            var rectangle = Between(Start, End);
                            g.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
                            break;
                    }
                }
            }
        }
    }
}
