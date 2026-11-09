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
        private readonly Bitmap desktop;
        private readonly Point desktopOrigin;
        private readonly List<Annotation> annotations = new List<Annotation>();
        private readonly Stack<Action> history = new Stack<Action>();
        private readonly StrokeStyle[] toolStrokes = { StrokeStyle.Default, StrokeStyle.Default, StrokeStyle.Default };
        private readonly Font annotationFont = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font menuFont = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Brush shade = new SolidBrush(Color.FromArgb(143, 8, 10, 20));
        private readonly Pen outline = new Pen(RadialMenu.Accent);
        private Rectangle selection;
        private Point anchor;
        private bool selecting;
        private bool selected;
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
                g.DrawRectangle(outline, selection.X, selection.Y, selection.Width - 1, selection.Height - 1);
            }
            if (selected && clip.IntersectsWith(toolHintArea))
                DrawToolHint(g);
            if (ring != null && clip.IntersectsWith(ring.Bounds))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                ring.Draw(g, tool);
            }
            if (status != null && clip.IntersectsWith(statusArea))
            {
                g.FillRectangle(Brushes.Black, statusArea);
                TextRenderer.DrawText(g, status, menuFont, Rectangle.Inflate(statusArea, -12, -6),
                    Color.White, TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
            }
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
                "Объект · " + (target.Tool == Tool.Frame ? "Рамка" : ToolInfo.Label(target.Tool));
            var textArea = Rectangle.Inflate(toolHintArea, -9, 0);
            using (var background = new SolidBrush(Color.FromArgb(240, 19, 24, 35)))
                g.FillRectangle(background, toolHintArea);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                TextFormatFlags.PreserveGraphicsClipping;
            if (shortcut.Length != 0)
            {
                var keyArea = new Rectangle(textArea.X, textArea.Y, 21, textArea.Height);
                TextRenderer.DrawText(g, shortcut, menuFont, keyArea, RadialMenu.Accent, flags);
                textArea.X += keyArea.Width;
                textArea.Width -= keyArea.Width;
            }
            textArea.Width = Math.Min(textArea.Width, 136 - (shortcut.Length == 0 ? 0 : 21));
            TextRenderer.DrawText(g, label, menuFont, textArea, Color.White, flags);
            if (HasStrokePalette) StrokePalette.Draw(g, toolHintArea, menuFont, CurrentStroke);
        }

        private void PlaceToolHint()
        {
            if (!selected) return;
            Invalidate(toolHintArea);
            Rectangle monitor = MonitorBounds(selection.Location);
            int width = HasStrokePalette ? StrokePalette.Width : 146;
            int height = HasStrokePalette ? StrokePalette.Height : 27;
            const int gap = 7;
            int x = Math.Max(monitor.Left, Math.Min(selection.Left, monitor.Right - width));
            int y = selection.Bottom + gap;
            if (y + height > monitor.Bottom)
                y = selection.Top - height - gap >= monitor.Top ? selection.Top - height - gap : monitor.Bottom - height;
            toolHintArea = new Rectangle(x, y, width, height);
            Invalidate(toolHintArea);
        }

        private void ChangeStroke(StrokeStyle style)
        {
            StrokeStyle before = CurrentStroke;
            if (before.Color.ToArgb() == style.Color.ToArgb() && before.Width == style.Width) return;
            Annotation target = StrokeObject;
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
            using (var border = new Pen(Color.DeepSkyBlue) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(border, bounds);
            foreach (Point handle in active.Handles())
            {
                var box = new Rectangle(handle.X - 3, handle.Y - 3, 7, 7);
                g.FillRectangle(Brushes.White, box);
                g.DrawRectangle(Pens.DeepSkyBlue, box);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Busy || ring != null) return;
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
                if (ring.Center != e.Location) Cursor.Position = PointToScreen(ring.Center);
                Invalidate(ring.Bounds);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            if (selected && toolHintArea.Contains(e.Location))
            {
                StrokeStyle style;
                if (HasStrokePalette && StrokePalette.TryPick(toolHintArea, e.Location, CurrentStroke, out style))
                    ChangeStroke(style);
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
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Busy) return;
            if (selecting) ChangeSelection(Annotation.Between(anchor, Clamp(e.Location, ClientRectangle)));
            else if (dragging) DragObject(e.Location);
            else if (pending != null) Invalidate(pending.Update(Clamp(e.Location, selection)));
            else if (ring != null)
            {
                int hover = ring.Hit(e.Location);
                if (hover == ring.Hover) return;
                ring.Hover = hover;
                Invalidate(ring.Bounds);
            }
            else UpdatePointer(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Busy && !dragging && ring == null) Cursor = Cursors.Cross;
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
            if (dragging)
            {
                DragObject(e.Location);
                FinishObjectDrag(true);
            }
            else if (selecting)
            {
                selecting = false;
                ChangeSelection(Annotation.Between(anchor, Clamp(e.Location, ClientRectangle)));
                selected = selection.Width > 1 && selection.Height > 1;
                if (!selected) ChangeSelection(Rectangle.Empty);
                else PlaceToolHint();
                Capture = false;
            }
            else if (pending != null)
            {
                Invalidate(pending.Update(Clamp(e.Location, selection)));
                AddAnnotation(pending);
                pending = null;
                Capture = false;
            }
            UpdatePointer(e.Location);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            if (ring != null) CloseRing();
            if (dragging) FinishObjectDrag(false);
            if (selecting)
            {
                selecting = false;
                ChangeSelection(Rectangle.Empty);
            }
            if (pending != null)
            {
                Invalidate(pending.PaintBounds());
                pending = null;
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
            if (selected && pending == null && !dragging)
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

        private void ChangeSelection(Rectangle next)
        {
            if (selection == next) return;
            using (var dirty = new Region(selection))
            {
                dirty.Xor(next);
                AddBorder(dirty, selection);
                AddBorder(dirty, next);
                selection = next;
                Invalidate(dirty);
            }
        }

        private static void AddBorder(Region dirty, Rectangle rectangle)
        {
            if (rectangle.IsEmpty) return;
            using (var border = new Region(rectangle))
            {
                Rectangle inside = Rectangle.Inflate(rectangle, -1, -1);
                if (inside.Width > 0 && inside.Height > 0) border.Exclude(inside);
                dirty.Union(border);
            }
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
            if (Busy || dragging || selecting || pending != null || ring != null) return;
            if (selected && textEditor == null && toolHintArea.Contains(point))
            {
                StrokeStyle ignored;
                Cursor = HasStrokePalette && StrokePalette.TryPick(toolHintArea, point, CurrentStroke, out ignored)
                    ? Cursors.Hand : Cursors.Default;
                return;
            }
            if (!selected || textEditor != null || !selection.Contains(point))
            {
                Cursor = Cursors.Cross;
                return;
            }

            int handle = HitHandle(point, active == null ? null : active.Handles());
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
            return true;
        }

        private void DragObject(Point point)
        {
            int dx = point.X - dragOrigin.X, dy = point.Y - dragOrigin.Y;
            if (!dragChanged && Math.Abs(dx) < 3 && Math.Abs(dy) < 3) return;
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
            textEditor = new TextBox { Font = annotationFont, MaxLength = 512, Location = new Point(x, y), Width = width,
                                       Text = annotation == null ? "" : annotation.Text };
            Controls.Add(textEditor);
            textEditor.Focus();
            if (annotation != null) textEditor.SelectAll();
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
                outline.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
