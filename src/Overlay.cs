using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GrayShotV2
{
    internal sealed class Overlay : Form
    {
        private const int GripRadius = 4;
        private const int LensRadius = 8, LensScale = 6, LensBorder = 2;
        private const int LensSize = (2 * LensRadius + 1) * LensScale + 2 * LensBorder;
        private static readonly Point[] GripDirections =
        {
            new Point(-1, -1), new Point(1, -1), new Point(1, 1), new Point(-1, 1),
            new Point(0, -1), new Point(1, 0), new Point(0, 1), new Point(-1, 0)
        };
        private static Color frameColor = StrokeStyle.Default.Color;
        private readonly Bitmap desktop;
        private readonly Point desktopOrigin;
        private readonly List<Annotation> annotations = new List<Annotation>();
        private readonly Stack<Action> history = new Stack<Action>();
        private readonly StrokeStyle[] toolStrokes = { StrokeStyle.Default, StrokeStyle.Default,
            new StrokeStyle(frameColor, StrokeStyle.Default.Width) };
        private readonly Font annotationFont = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font menuFont = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Brush shade = new SolidBrush(Color.FromArgb(128, Color.Black));
        private WindowsAppearance appearance = WindowsAppearance.Read();
        private Rectangle selection;
        private Point anchor;
        private bool selecting;
        private bool selected;
        private Point resizeDirection, resizeOrigin;
        private Rectangle selectionBeforeResize;
        private Point lensPoint;
        private Rectangle lensArea;
        private Rectangle selectionSizeArea, frameSizeArea;
        private string selectionSizeText, frameSizeText;
        private Tool tool;
        private Annotation pending;
        private Annotation active;
        private Point dragOrigin, originalStart, originalEnd, resizeAnchor, originalHandle;
        private Rectangle originalBounds;
        private int dragHandle = -1;
        private bool dragging, dragChanged, editTextOnClick;
        private RadialMenu ring;
        private Point ringOrigin;
        private TextBox textEditor;
        private Point textAnchor;
        private Annotation editedText;
        private string status;
        private Rectangle statusArea;
        private Rectangle toolHintArea;
        private Annotation StrokeObject
        {
            get { return active != null && ToolInfo.SupportsStroke(active.Tool) ? active : null; }
        }
        private bool HasStrokePalette { get { return StrokeObject != null || ToolInfo.SupportsStroke(tool); } }
        private StrokeStyle CurrentStroke { get { return StrokeObject == null ? toolStrokes[(int)tool] : StrokeObject.Stroke; } }
        public bool Busy { get; private set; }

        public Overlay(Bitmap desktop, Rectangle bounds)
        {
            this.desktop = desktop;
            desktopOrigin = bounds.Location;
            Text = "GrayShotV2 — Снимок";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = bounds;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int systemColorsChanged = 0x0015, settingsChanged = 0x001A, themeChanged = 0x031A;
            if (m.Msg == systemColorsChanged || m.Msg == settingsChanged || m.Msg == themeChanged)
            {
                appearance = WindowsAppearance.Read();
                if (textEditor != null) ApplyTextEditorAppearance();
                Invalidate();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RefreshPointer();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = Rectangle.Intersect(ClientRectangle, e.ClipRectangle);
            if (clip.IsEmpty) return;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImage(desktop, clip, clip, GraphicsUnit.Pixel);
            using (var outside = new Region(clip))
            {
                if (!selection.IsEmpty) outside.Exclude(selection);
                g.FillRegion(shade, outside);
            }

            if (clip.IntersectsWith(selection))
            {
                GraphicsState state = g.Save();
                g.SetClip(selection, CombineMode.Intersect);
                DrawAnnotations(g, clip, true);
                if (active != null && active != editedText && clip.IntersectsWith(active.PaintBounds()))
                    DrawObjectSelection(g);
                g.Restore(state);
                g.DrawRectangle(SystemPens.Highlight, selection.X, selection.Y, selection.Width - 1, selection.Height - 1);
            }
            if (selected && clip.IntersectsWith(toolHintArea))
                DrawToolHint(g);
            if (selected) DrawSelectionGrips(g, clip);
            if (clip.IntersectsWith(lensArea)) DrawLens(g);
            if (clip.IntersectsWith(selectionSizeArea)) DrawSizeLabel(g, selectionSizeArea, selectionSizeText);
            if (clip.IntersectsWith(frameSizeArea)) DrawSizeLabel(g, frameSizeArea, frameSizeText);
            if (ring != null && clip.IntersectsWith(ring.Bounds))
                ring.Draw(g, tool, appearance, DrawBlur);
            if (status != null && clip.IntersectsWith(statusArea))
            {
                DrawPanel(g, statusArea);
                TextRenderer.DrawText(g, status, menuFont, Rectangle.Inflate(statusArea, -12, -6),
                    appearance.Text, TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
            }
        }

        private void DrawLens(Graphics g)
        {
            var sample = new Rectangle(lensPoint.X - LensRadius, lensPoint.Y - LensRadius,
                2 * LensRadius + 1, 2 * LensRadius + 1);
            Rectangle visible = Rectangle.Intersect(sample, new Rectangle(Point.Empty, desktop.Size));
            int x = lensArea.X + LensBorder, y = lensArea.Y + LensBorder;
            var target = new Rectangle(x + (visible.X - sample.X) * LensScale,
                y + (visible.Y - sample.Y) * LensScale, visible.Width * LensScale, visible.Height * LensScale);

            GraphicsState state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var background = new SolidBrush(appearance.Surface))
                g.FillRectangle(background, lensArea);
            g.DrawImage(desktop, target, visible, GraphicsUnit.Pixel);
            g.PixelOffsetMode = PixelOffsetMode.Default;

            int centerX = x + LensRadius * LensScale, centerY = y + LensRadius * LensScale;
            g.DrawRectangle(Pens.Black, centerX - 1, centerY - 1, LensScale + 1, LensScale + 1);
            g.DrawRectangle(Pens.White, centerX, centerY, LensScale - 1, LensScale - 1);
            g.DrawRectangle(SystemPens.Highlight, lensArea.X, lensArea.Y, LensSize - 1, LensSize - 1);
            g.Restore(state);
        }

        private void UpdateLens(Point point, bool show)
        {
            Rectangle next = Rectangle.Empty;
            if (show && ClientRectangle.Contains(point))
            {
                Rectangle monitor = MonitorBounds(point);
                if (monitor.Contains(point) && monitor.Width >= LensSize && monitor.Height >= LensSize)
                {
                    const int gap = 22;
                    int x = point.X + gap + LensSize <= monitor.Right ? point.X + gap : point.X - gap - LensSize;
                    int y = point.Y + gap + LensSize <= monitor.Bottom ? point.Y + gap : point.Y - gap - LensSize;
                    next = new Rectangle(Math.Max(monitor.Left, Math.Min(x, monitor.Right - LensSize)),
                        Math.Max(monitor.Top, Math.Min(y, monitor.Bottom - LensSize)), LensSize, LensSize);
                }
            }
            if (lensArea == next && (next.IsEmpty || lensPoint == point)) return;
            if (!lensArea.IsEmpty) Invalidate(lensArea);
            lensArea = next;
            lensPoint = point;
            if (!lensArea.IsEmpty) Invalidate(lensArea);
        }

        private void DrawPanel(Graphics g, Rectangle area)
        {
            if (appearance.Transparency)
                DrawBlur(g, area);
            using (var fill = new SolidBrush(appearance.SurfaceTint))
                g.FillRectangle(fill, area);
            using (var border = new Pen(appearance.Border))
                g.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - 1);
        }

        private void DrawSizeLabel(Graphics g, Rectangle area, string text)
        {
            DrawPanel(g, area);
            TextRenderer.DrawText(g, text, menuFont, area, appearance.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
        }

        private void UpdateSizeLabels()
        {
            UpdateSizeLabel(selection, selected ? GripFrame(selection) : selection, Rectangle.Empty,
                ref selectionSizeArea, ref selectionSizeText);

            Annotation frame = pending ?? active;
            Rectangle bounds = frame != null && frame.Tool == Tool.Frame ? frame.Bounds : Rectangle.Empty;
            UpdateSizeLabel(bounds, Rectangle.Intersect(bounds, selection), selectionSizeArea,
                ref frameSizeArea, ref frameSizeText);
        }

        private void UpdateSizeLabel(Rectangle bounds, Rectangle edge, Rectangle other,
            ref Rectangle area, ref string text)
        {
            string nextText = null;
            Rectangle next = Rectangle.Empty;
            if (edge.Width > 0 && edge.Height > 0)
            {
                nextText = bounds.Width + " × " + bounds.Height + " px";
                int width = nextText == text ? area.Width :
                    TextRenderer.MeasureText(nextText, menuFont, Size.Empty, TextFormatFlags.NoPadding).Width + 12;
                next = PlaceSizeLabel(edge, width, other);
            }
            if (area == next && text == nextText) return;
            if (!area.IsEmpty) Invalidate(area);
            area = next;
            text = nextText;
            if (!area.IsEmpty) Invalidate(area);
        }

        private Rectangle PlaceSizeLabel(Rectangle edge, int width, Rectangle other)
        {
            const int height = 22, gap = 8;
            Rectangle monitor = MonitorBounds(edge.Location);
            int left = Math.Max(monitor.Left, Math.Min(edge.Left, monitor.Right - width));
            int right = Math.Max(monitor.Left, Math.Min(edge.Right - width, monitor.Right - width));
            int inside = Math.Max(monitor.Top, Math.Min(edge.Top + gap, monitor.Bottom - height));
            foreach (int y in new[] { edge.Top - height - gap, edge.Bottom + gap, inside })
            {
                if (y < monitor.Top || y + height > monitor.Bottom) continue;
                var label = new Rectangle(left, y, width, height);
                if (SizeLabelFits(label, other)) return label;
                label.X = right;
                if (SizeLabelFits(label, other)) return label;
            }
            int middle = Math.Max(monitor.Top, Math.Min(edge.Top + (edge.Height - height) / 2, monitor.Bottom - height));
            var beside = new Rectangle(edge.Right + gap, middle, width, height);
            if (monitor.Contains(beside) && SizeLabelFits(beside, other)) return beside;
            beside.X = edge.Left - width - gap;
            if (monitor.Contains(beside) && SizeLabelFits(beside, other)) return beside;
            return new Rectangle(left, inside, width, height);
        }

        private bool SizeLabelFits(Rectangle area, Rectangle other)
        {
            return !area.IntersectsWith(other) && !area.IntersectsWith(lensArea) &&
                !area.IntersectsWith(toolHintArea);
        }

        private void DrawAnnotations(Graphics g, Rectangle clip, bool preview)
        {
            // Blur the screenshot layer so existing annotations stay sharp.
            foreach (Annotation annotation in annotations)
                if (annotation.Tool == Tool.Blur && clip.IntersectsWith(annotation.Bounds))
                    DrawBlur(g, annotation.Bounds);
            if (preview && pending != null && pending.Tool == Tool.Blur && clip.IntersectsWith(pending.Bounds))
                DrawBlur(g, pending.Bounds);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (Annotation annotation in annotations)
                if (annotation.Tool != Tool.Blur && annotation != editedText && clip.IntersectsWith(annotation.PaintBounds()))
                    annotation.Draw(g, annotationFont);
            if (preview && pending != null && pending.Tool != Tool.Blur && clip.IntersectsWith(pending.PaintBounds()))
                pending.Draw(g, annotationFont);
        }

        private void DrawBlur(Graphics g, Rectangle area)
        {
            area = Rectangle.Intersect(area, new Rectangle(Point.Empty, desktop.Size));
            if (area.Width == 0 || area.Height == 0) return;
            const int blurScale = 14;
            int width = (area.Width + blurScale - 1) / blurScale;
            int height = (area.Height + blurScale - 1) / blurScale;
            using (var sample = new Bitmap(width, height, PixelFormat.Format32bppRgb))
            {
                using (var downsample = Graphics.FromImage(sample))
                {
                    downsample.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    downsample.DrawImage(desktop, new Rectangle(0, 0, width, height), area, GraphicsUnit.Pixel);
                }
                GraphicsState state = g.Save();
                g.SetClip(area, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.Bilinear;
                using (var edges = new ImageAttributes())
                {
                    edges.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(sample, area, 0, 0, width, height, GraphicsUnit.Pixel, edges);
                }
                g.Restore(state);
            }
        }

        private void DrawToolHint(Graphics g)
        {
            Annotation target = StrokeObject;
            string shortcut = target == null ? ToolInfo.Shortcut(tool) : "";
            string label = target == null ? ToolInfo.Label(tool) :
                "Объект · " + ToolInfo.Label(target.Tool);
            var textArea = Rectangle.Inflate(toolHintArea, -9, 0);
            DrawPanel(g, toolHintArea);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.PreserveGraphicsClipping;
            if (shortcut.Length != 0)
            {
                var keyArea = new Rectangle(textArea.X, textArea.Y, 21, textArea.Height);
                TextRenderer.DrawText(g, shortcut, menuFont, keyArea, appearance.Text, flags);
                textArea.X += keyArea.Width;
                textArea.Width -= keyArea.Width;
            }
            textArea.Width = Math.Min(textArea.Width, 136 - (shortcut.Length == 0 ? 0 : 21));
            TextRenderer.DrawText(g, label, menuFont, textArea, appearance.Text, flags);
            if (HasStrokePalette) StrokePalette.Draw(g, toolHintArea, menuFont, CurrentStroke, appearance);
        }

        private void PlaceToolHint()
        {
            if (!selected) return;
            Invalidate(toolHintArea);
            Rectangle monitor = MonitorBounds(selection.Location);
            int width = HasStrokePalette ? StrokePalette.Width : 146;
            int height = HasStrokePalette ? StrokePalette.Height : 27;
            const int gap = 7;
            Rectangle frame = GripFrame(selection);
            int x = Math.Max(monitor.Left, Math.Min(selection.Left, monitor.Right - width));
            int y = frame.Bottom + gap;
            if (y + height > monitor.Bottom)
                y = frame.Top - height - gap >= monitor.Top ? frame.Top - height - gap : monitor.Bottom - height;
            toolHintArea = new Rectangle(x, y, width, height);
            Invalidate(toolHintArea);
        }

        private void ChangeStroke(StrokeStyle style, bool colorPicked)
        {
            StrokeStyle before = CurrentStroke;
            Annotation target = StrokeObject;
            if (colorPicked && (target == null ? tool : target.Tool) == Tool.Frame)
            {
                frameColor = style.Color;
                toolStrokes[(int)Tool.Frame] = new StrokeStyle(frameColor, toolStrokes[(int)Tool.Frame].Width);
            }
            if (before.Color.ToArgb() == style.Color.ToArgb() && before.Width == style.Width) return;
            if (target == null) toolStrokes[(int)tool] = style;
            else
            {
                SetStroke(target, style);
                history.Push(delegate { SetStroke(target, before); });
            }
            Invalidate(toolHintArea);
        }

        private void SetStroke(Annotation target, StrokeStyle style)
        {
            Rectangle before = target.PaintBounds();
            target.Stroke = style;
            Invalidate(Rectangle.Union(before, target.PaintBounds()));
            Invalidate(toolHintArea);
        }

        private void DrawObjectSelection(Graphics g)
        {
            var bounds = Rectangle.Inflate(active.Bounds, 4, 4);
            using (var border = new Pen(SystemColors.Highlight) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(border, bounds);
            using (var fill = new SolidBrush(appearance.Inset))
            {
                foreach (Point handle in active.Handles())
                {
                    var box = new Rectangle(handle.X - 3, handle.Y - 3, 7, 7);
                    g.FillRectangle(fill, box);
                    g.DrawRectangle(SystemPens.Highlight, box.X, box.Y, box.Width - 1, box.Height - 1);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Busy || ring != null || !resizeDirection.IsEmpty) return;
            if (textEditor != null)
            {
                FinishText(true);
                if (e.Button != MouseButtons.Right) return;
            }
            if (e.Button == MouseButtons.Right && selected && pending == null && !dragging)
            {
                ringOrigin = PointToScreen(e.Location);
                ring = new RadialMenu(e.Location, MonitorBounds(e.Location));
                Capture = true;
                Cursor = Cursors.Default;
                UpdateLens(e.Location, false);
                UpdateSizeLabels();
                if (ring.Center != e.Location) Cursor.Position = PointToScreen(ring.Center);
                Invalidate(ring.Bounds);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            Point grip = HitSelectionGrip(e.Location);
            if (!grip.IsEmpty)
            {
                selectionBeforeResize = selection;
                resizeOrigin = e.Location;
                resizeDirection = grip;
                Capture = true;
                UpdatePointer(e.Location);
                return;
            }
            if (selected && toolHintArea.Contains(e.Location))
            {
                StrokeStyle style;
                bool colorPicked;
                if (HasStrokePalette && StrokePalette.TryPick(toolHintArea, e.Location, CurrentStroke, out style, out colorPicked))
                    ChangeStroke(style, colorPicked);
                return;
            }
            if (!selected)
            {
                selecting = Capture = true;
                anchor = e.Location;
                ChangeSelection(Annotation.Between(anchor, anchor));
            }
            else if (selection.Contains(e.Location))
            {
                if (BeginObjectDrag(e.Location)) return;
                SetActive(null);
                if (tool == Tool.Text) BeginText(e.Location);
                else
                {
                    pending = new Annotation(tool, e.Location);
                    if (ToolInfo.SupportsStroke(tool)) pending.Stroke = toolStrokes[(int)tool];
                    Capture = true;
                    Invalidate(pending.PaintBounds());
                }
            }
            else SetActive(null);
            UpdatePointer(e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            MovePointer(e.Location);
        }

        private void MovePointer(Point point)
        {
            if (Busy) return;
            if (selecting) ChangeSelection(Annotation.Between(anchor, Clamp(point, ClientRectangle)));
            else if (!resizeDirection.IsEmpty) ResizeSelection(point);
            else if (dragging) DragObject(point);
            else if (pending != null) Invalidate(pending.Update(Clamp(point, selection)));
            else if (ring != null)
            {
                int hover = ring.Hit(point);
                if (hover == ring.Hover) return;
                ring.Hover = hover;
                Invalidate(ring.Bounds);
            }
            UpdatePointer(point);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            UpdateLens(Point.Empty, false);
            UpdateSizeLabels();
            if (!Busy && !dragging && resizeDirection.IsEmpty && ring == null) Cursor = Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (ring != null)
            {
                if (e.Button == MouseButtons.Right)
                {
                    // A quick tap near an edge can queue its release before the cursor is centered.
                    bool unmovedTap = ring.Hover < 0 && e.Location == PointToClient(ringOrigin);
                    int item = unmovedTap ? -1 : ring.Hit(e.Location);
                    CloseRing();
                    if (item >= 0) SelectTool((Tool)item);
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (!resizeDirection.IsEmpty)
            {
                ResizeSelection(e.Location);
                FinishSelectionResize(true);
            }
            else if (dragging)
            {
                DragObject(e.Location);
                FinishObjectDrag(true);
            }
            else if (selecting)
            {
                selecting = false;
                ChangeSelection(Annotation.Between(anchor, Clamp(e.Location, ClientRectangle)));
                selected = selection.Width > 1 || selection.Height > 1;
                if (!selected) ChangeSelection(Rectangle.Empty);
                else
                {
                    Invalidate(Rectangle.Inflate(GripFrame(selection), GripRadius, GripRadius));
                    PlaceToolHint();
                }
                Capture = false;
            }
            else if (pending != null)
            {
                Annotation annotation = pending;
                Invalidate(annotation.Update(Clamp(e.Location, selection)));
                AddAnnotation(annotation);
                pending = null;
                if (annotation.Tool == Tool.Frame) SetActive(annotation);
                Capture = false;
            }
            UpdatePointer(e.Location);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            if (ring != null) CloseRing();
            if (!resizeDirection.IsEmpty) FinishSelectionResize(false);
            if (dragging) FinishObjectDrag(false);
            if (selecting)
            {
                selecting = false;
                ChangeSelection(Rectangle.Empty);
                UpdateSizeLabels();
            }
            if (pending != null)
            {
                Invalidate(pending.PaintBounds());
                pending = null;
                RefreshPointer();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Busy) return true;
            if (keyData == Keys.Escape)
            {
                if (textEditor != null)
                {
                    FinishText(false);
                    RefreshPointer();
                }
                else if (ring != null) CloseRing();
                else if (!resizeDirection.IsEmpty) FinishSelectionResize(false);
                else if (dragging) FinishObjectDrag(false);
                else if (active != null)
                {
                    SetActive(null);
                    RefreshPointer();
                }
                else Close();
                return true;
            }
            if (textEditor != null)
            {
                if (keyData != Keys.Enter) return base.ProcessCmdKey(ref msg, keyData);
                FinishText(true);
                RefreshPointer();
                return true;
            }
            if (ring == null && MoveCursorByKey(keyData)) return true;
            if (selected && pending == null && !dragging && resizeDirection.IsEmpty)
            {
                Tool shortcut;
                if ((keyData & Keys.Modifiers) == Keys.None && ToolInfo.TryShortcut((char)keyData, out shortcut))
                {
                    CloseRing();
                    SelectTool(shortcut);
                    return true;
                }
                if (ring != null) return true;
                if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
                if (keyData == Keys.Delete && active != null) { DeleteActive(); return true; }
                if (keyData == (Keys.Control | Keys.C) || keyData == Keys.Enter) { Copy(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (Busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        }

        private bool MoveCursorByKey(Keys key)
        {
            Point step;
            switch (key)
            {
                case Keys.Left: step = new Point(-1, 0); break;
                case Keys.Right: step = new Point(1, 0); break;
                case Keys.Up: step = new Point(0, -1); break;
                case Keys.Down: step = new Point(0, 1); break;
                default: return false;
            }
            Point point = PointToClient(Cursor.Position);
            point.Offset(step);
            Cursor.Position = PointToScreen(Clamp(point, ClientRectangle));
            MovePointer(PointToClient(Cursor.Position));
            return true;
        }

        private Rectangle GripFrame(Rectangle bounds)
        {
            // Keep the grips reachable when the capture is only a few pixels wide or tall.
            const int minimum = 4 * GripRadius + 1;
            int width = Math.Min(ClientSize.Width, Math.Max(bounds.Width, minimum));
            int height = Math.Min(ClientSize.Height, Math.Max(bounds.Height, minimum));
            int x = Math.Max(0, Math.Min(bounds.Left - (width - bounds.Width) / 2, ClientSize.Width - width));
            int y = Math.Max(0, Math.Min(bounds.Top - (height - bounds.Height) / 2, ClientSize.Height - height));
            return new Rectangle(x, y, width, height);
        }

        private static Rectangle GripBox(Rectangle frame, Point direction)
        {
            int x = frame.Left + (direction.X + 1) * (frame.Width - 1) / 2;
            int y = frame.Top + (direction.Y + 1) * (frame.Height - 1) / 2;
            return new Rectangle(x - GripRadius, y - GripRadius, 2 * GripRadius + 1, 2 * GripRadius + 1);
        }

        private void DrawSelectionGrips(Graphics g, Rectangle clip)
        {
            Rectangle frame = GripFrame(selection);
            using (var fill = new SolidBrush(appearance.Inset))
            {
                foreach (Point direction in GripDirections)
                {
                    Rectangle box = GripBox(frame, direction);
                    if (!clip.IntersectsWith(box)) continue;
                    g.FillRectangle(fill, box);
                    g.DrawRectangle(SystemPens.Highlight, box.X, box.Y, box.Width - 1, box.Height - 1);
                }
            }
        }

        private Point HitSelectionGrip(Point point)
        {
            if (!selected) return Point.Empty;
            Rectangle frame = GripFrame(selection);
            Point nearest = Point.Empty;
            int best = int.MaxValue;
            foreach (Point direction in GripDirections)
            {
                Rectangle box = GripBox(frame, direction);
                if (!Rectangle.Inflate(box, 1, 1).Contains(point)) continue;
                int dx = point.X - box.X - GripRadius, dy = point.Y - box.Y - GripRadius;
                int distance = dx * dx + dy * dy;
                if (distance >= best) continue;
                nearest = direction;
                best = distance;
            }
            return nearest;
        }

        private static Cursor GripCursor(Point direction)
        {
            if (direction.X == 0) return Cursors.SizeNS;
            if (direction.Y == 0) return Cursors.SizeWE;
            return direction.X == direction.Y ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }

        private void ResizeSelection(Point point)
        {
            int dx = point.X - resizeOrigin.X, dy = point.Y - resizeOrigin.Y;
            int left = selectionBeforeResize.Left, top = selectionBeforeResize.Top;
            int right = selectionBeforeResize.Right, bottom = selectionBeforeResize.Bottom;
            if (resizeDirection.X < 0) left = Math.Max(0, Math.Min(left + dx, right - 1));
            if (resizeDirection.X > 0) right = Math.Max(left + 1, Math.Min(right + dx, ClientSize.Width));
            if (resizeDirection.Y < 0) top = Math.Max(0, Math.Min(top + dy, bottom - 1));
            if (resizeDirection.Y > 0) bottom = Math.Max(top + 1, Math.Min(bottom + dy, ClientSize.Height));
            ChangeSelection(Rectangle.FromLTRB(left, top, right, bottom));
        }

        private void FinishSelectionResize(bool commit)
        {
            Rectangle before = selectionBeforeResize;
            resizeDirection = Point.Empty;
            Capture = false;
            if (!commit) ChangeSelection(before);
            else if (selection != before) history.Push(delegate { ChangeSelection(before); });
            RefreshPointer();
        }

        private void ChangeSelection(Rectangle next)
        {
            if (selection == next) return;
            using (var dirty = new Region(selection))
            {
                dirty.Xor(next);
                AddSelectionBorder(dirty, selection);
                AddSelectionBorder(dirty, next);
                selection = next;
                Invalidate(dirty);
            }
            if (selected) PlaceToolHint();
        }

        private void AddSelectionBorder(Region dirty, Rectangle rectangle)
        {
            if (rectangle.IsEmpty) return;
            using (var border = new Region(rectangle))
            {
                Rectangle inside = Rectangle.Inflate(rectangle, -1, -1);
                if (inside.Width > 0 && inside.Height > 0) border.Exclude(inside);
                dirty.Union(border);
            }
            if (!selected) return;
            Rectangle frame = GripFrame(rectangle);
            foreach (Point direction in GripDirections) dirty.Union(GripBox(frame, direction));
        }

        private Rectangle MonitorBounds(Point clientPoint)
        {
            Point screen = clientPoint;
            screen.Offset(desktopOrigin);
            Rectangle monitor = Screen.FromPoint(screen).Bounds;
            monitor.Offset(-desktopOrigin.X, -desktopOrigin.Y);
            return monitor;
        }

        private static Point Clamp(Point p, Rectangle rect)
        {
            return new Point(Math.Max(rect.Left, Math.Min(p.X, rect.Right - 1)),
                             Math.Max(rect.Top, Math.Min(p.Y, rect.Bottom - 1)));
        }

        private void CloseRing()
        {
            if (ring == null) return;
            Rectangle dirty = ring.Bounds;
            ring.Dispose();
            ring = null;
            Cursor.Position = ringOrigin;
            Capture = false;
            RefreshPointer();
            Invalidate(dirty);
        }

        private void SelectTool(Tool item)
        {
            if (item == Tool.Copy) Copy();
            else if (item == Tool.Undo) Undo();
            else
            {
                tool = item;
                if (active != null) SetActive(null);
                else PlaceToolHint();
                RefreshPointer();
            }
        }

        private void Undo()
        {
            if (history.Count != 0) history.Pop()();
            RefreshPointer();
        }

        private void AddAnnotation(Annotation annotation)
        {
            annotations.Add(annotation);
            history.Push(delegate
            {
                if (active == annotation) SetActive(null);
                annotations.Remove(annotation);
                Invalidate(annotation.PaintBounds());
            });
        }

        private void SetActive(Annotation annotation)
        {
            if (annotation == active) return;
            if (active != null) Invalidate(active.PaintBounds());
            active = annotation;
            if (active != null) Invalidate(active.PaintBounds());
            PlaceToolHint();
        }

        private void DeleteActive()
        {
            Annotation annotation = active;
            int index = annotations.IndexOf(annotation);
            SetActive(null);
            annotations.RemoveAt(index);
            history.Push(delegate
            {
                annotations.Insert(index, annotation);
                SetActive(annotation);
            });
            RefreshPointer();
        }

        private static int HitHandle(Point point, Point[] handles)
        {
            if (handles == null) return -1;
            for (int i = 0; i < handles.Length; i++)
                if (Math.Abs(point.X - handles[i].X) <= 5 && Math.Abs(point.Y - handles[i].Y) <= 5)
                    return i;
            return -1;
        }

        private Annotation HitAnnotation(Point point)
        {
            Annotation blur = null;
            for (int i = annotations.Count - 1; i >= 0; i--)
            {
                Annotation annotation = annotations[i];
                if (!annotation.Hit(point)) continue;
                if (annotation.Tool != Tool.Blur) return annotation;
                if (blur == null) blur = annotation;
            }
            return blur;
        }

        private static Cursor HandlePointer(Annotation annotation, int handle)
        {
            if (annotation.Tool == Tool.Arrow) return Cursors.Hand;
            return handle % 2 == 0 ? Cursors.SizeNWSE : Cursors.SizeNESW;
        }

        private void RefreshPointer()
        {
            UpdatePointer(PointToClient(Cursor.Position));
        }

        private void UpdatePointer(Point point)
        {
            UpdatePointerAppearance(point);
            UpdateSizeLabels();
        }

        private void UpdatePointerAppearance(Point point)
        {
            if (Busy || ring != null || textEditor != null)
            {
                UpdateLens(point, false);
                return;
            }
            if (dragging || pending != null)
            {
                UpdateLens(point, (dragging ? active : pending).Tool == Tool.Frame);
                return;
            }
            Point grip = resizeDirection.IsEmpty ? HitSelectionGrip(point) : resizeDirection;
            if (selecting || !grip.IsEmpty)
            {
                UpdateLens(point, true);
                if (!grip.IsEmpty) Cursor = GripCursor(grip);
                return;
            }
            if (selected && toolHintArea.Contains(point))
            {
                UpdateLens(point, false);
                StrokeStyle ignored;
                bool ignoredColor;
                Cursor = HasStrokePalette && StrokePalette.TryPick(toolHintArea, point, CurrentStroke, out ignored, out ignoredColor)
                    ? Cursors.Hand : Cursors.Default;
                return;
            }
            if (!selected || !selection.Contains(point))
            {
                UpdateLens(point, !selected);
                Cursor = Cursors.Cross;
                return;
            }

            int handle = HitHandle(point, active == null ? null : active.Handles());
            UpdateLens(point, tool == Tool.Frame || (handle >= 0 && active.Tool == Tool.Frame));
            if (handle >= 0) Cursor = HandlePointer(active, handle);
            else Cursor = HitAnnotation(point) == null ? Cursors.Cross : Cursors.SizeAll;
        }

        private bool BeginObjectDrag(Point point)
        {
            Point[] handles = active == null ? null : active.Handles();
            dragHandle = HitHandle(point, handles);
            Annotation hit = dragHandle >= 0 ? active : HitAnnotation(point);
            if (hit == null) return false;
            if (dragHandle >= 0)
            {
                originalHandle = handles[dragHandle];
                resizeAnchor = handles[(dragHandle + handles.Length / 2) % handles.Length];
            }

            editTextOnClick = hit == active && hit.Tool == Tool.Text;
            SetActive(hit);
            dragOrigin = point;
            originalStart = hit.Start;
            originalEnd = hit.End;
            originalBounds = hit.Bounds;
            dragChanged = false;
            dragging = Capture = true;
            Cursor = dragHandle < 0 ? Cursors.SizeAll : HandlePointer(hit, dragHandle);
            UpdatePointer(point);
            return true;
        }

        private void DragObject(Point point)
        {
            int dx = point.X - dragOrigin.X, dy = point.Y - dragOrigin.Y;
            int threshold = active.Tool == Tool.Frame ? 1 : 3;
            if (!dragChanged && Math.Abs(dx) < threshold && Math.Abs(dy) < threshold) return;
            dragChanged = true;
            Rectangle dirty = active.PaintBounds();
            if (dragHandle < 0)
            {
                dx = LimitOffset(dx, originalBounds.Left, originalBounds.Right, selection.Left, selection.Right);
                dy = LimitOffset(dy, originalBounds.Top, originalBounds.Bottom, selection.Top, selection.Bottom);
                active.Move(originalStart.X + dx - active.Start.X, originalStart.Y + dy - active.Start.Y);
            }
            else
            {
                Point next = Clamp(new Point(originalHandle.X + dx, originalHandle.Y + dy), selection);
                if (active.Tool == Tool.Arrow && dragHandle == 0) active.SetEndpoints(next, resizeAnchor);
                else active.SetEndpoints(resizeAnchor, next);
            }
            Invalidate(Rectangle.Union(dirty, active.PaintBounds()));
        }

        private static int LimitOffset(int offset, int start, int end, int min, int max)
        {
            int low = min - start;
            int high = end - start <= max - min ? max - end : max - 1 - start;
            return Math.Max(low, Math.Min(high, offset));
        }

        private void FinishObjectDrag(bool commit)
        {
            Annotation annotation = active;
            Point start = originalStart, end = originalEnd;
            bool resize = dragHandle >= 0;
            if (annotation.Start != start || annotation.End != end)
            {
                Action restore = delegate
                {
                    Rectangle dirty = annotation.PaintBounds();
                    if (resize) annotation.SetEndpoints(start, end);
                    else annotation.Move(start.X - annotation.Start.X, start.Y - annotation.Start.Y);
                    Invalidate(Rectangle.Union(dirty, annotation.PaintBounds()));
                };
                if (commit) history.Push(restore);
                else restore();
            }
            dragging = false;
            Capture = false;
            if (commit && !dragChanged && editTextOnClick) BeginText(annotation.Start, annotation);
            RefreshPointer();
        }

        private void BeginText(Point point, Annotation annotation = null)
        {
            textAnchor = point;
            editedText = annotation;
            if (annotation != null) Invalidate(annotation.PaintBounds());
            Rectangle monitor = MonitorBounds(point);
            const int width = 240;
            int x = Math.Max(monitor.Left, Math.Min(point.X, monitor.Right - width));
            int y = Math.Max(monitor.Top, Math.Min(point.Y, monitor.Bottom - 34));
            textEditor = new TextBox { Font = annotationFont, BorderStyle = BorderStyle.FixedSingle,
                                       MaxLength = 512, Location = new Point(x, y), Width = width,
                                       Text = annotation == null ? "" : annotation.Text };
            ApplyTextEditorAppearance();
            Controls.Add(textEditor);
            textEditor.Focus();
            if (annotation != null) textEditor.SelectAll();
        }

        private void ApplyTextEditorAppearance()
        {
            textEditor.BackColor = appearance.Inset;
            textEditor.ForeColor = appearance.Text;
        }

        private void FinishText(bool commit)
        {
            string value = textEditor.Text;
            Annotation annotation = editedText;
            editedText = null;
            textEditor.Dispose();
            textEditor = null;
            Focus();
            if (annotation != null) Invalidate(annotation.PaintBounds());
            if (!commit || string.IsNullOrWhiteSpace(value)) return;

            Size size;
            using (var graphics = CreateGraphics())
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                size = Size.Ceiling(graphics.MeasureString(value, annotationFont, int.MaxValue, StringFormat.GenericTypographic));
            }
            if (annotation == null)
            {
                annotation = new Annotation(Tool.Text, textAnchor);
                AddAnnotation(annotation);
            }
            else
            {
                if (value == annotation.Text) return;
                string original = annotation.Text;
                Size originalSize = annotation.Bounds.Size;
                history.Push(delegate
                {
                    Rectangle dirty = annotation.PaintBounds();
                    annotation.SetText(original, originalSize);
                    Invalidate(Rectangle.Union(dirty, annotation.PaintBounds()));
                });
            }
            annotation.SetText(value, size);
            Invalidate(annotation.PaintBounds());
        }

        private void ShowStatus(string message)
        {
            if (status != null) Invalidate(statusArea);
            status = message;
            Rectangle monitor = MonitorBounds(selection.Location);
            statusArea = new Rectangle(monitor.Left + 16, monitor.Top + 16, Math.Min(620, monitor.Width - 32), 60);
            Invalidate(statusArea);
        }

        private Bitmap RenderResult()
        {
            var result = desktop.Clone(selection, PixelFormat.Format32bppRgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.TranslateTransform(-selection.X, -selection.Y);
                    DrawAnnotations(graphics, selection, false);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        private async void Copy()
        {
            if (Busy) return;
            Busy = true;
            CloseRing();
            Cursor = Cursors.WaitCursor;
            UpdateLens(Point.Empty, false);
            DateTime timestamp = DateTime.Now;
            string path = null;
            string failure = null;
            try
            {
                ShowStatus("Сохраняем снимок…");
                using (Bitmap result = RenderResult())
                using (MemoryStream dib = await Task.Run(() =>
                {
                    MemoryStream encoded = ShotExport.EncodeDib(result);
                    try
                    {
                        path = ShotExport.SavePng(result, ShotExport.PicturesRoot(), timestamp);
                        return encoded;
                    }
                    catch
                    {
                        encoded.Dispose();
                        throw;
                    }
                }))
                {
                    await ShotExport.CopyDib(dib, Handle);
                }
            }
            catch (Exception error)
            {
                failure = "Не удалось скопировать. " + error.Message;
            }
            if (failure != null && path != null)
            {
                try { await Task.Run(() => File.Delete(path)); }
                catch (Exception) { failure += " PNG остался здесь: " + path; }
            }
            Busy = false;
            RefreshPointer();
            if (failure == null) Close();
            else ShowStatus(failure);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (ring != null) ring.Dispose();
                desktop.Dispose();
                annotationFont.Dispose();
                menuFont.Dispose();
                shade.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
