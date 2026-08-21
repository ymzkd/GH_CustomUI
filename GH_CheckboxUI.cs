using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// 単一のチェックボックスUI
    /// 既定は GroupTogglesUI形式のレイアウト（ラベルが上、チェックボックスが下）
    /// Orientation を Horizontal にするとラベルが左、チェックボックスが右の横並びになる
    /// </summary>
    public class CheckboxUI : GH_UIParts
    {
        public bool Checked { get; set; } = false;
        public string Label;

        public float Margin = 2f;

        /// <summary>ラベルとチェックボックスの並び</summary>
        public LabelOrientation Orientation { get; set; } = LabelOrientation.Vertical;

        /// <summary>横並び時のラベルとチェックボックスの間隔</summary>
        public float LabelSpacing = 4f;

        private Font label_font = GH_FontServer.Standard;
        private float label_height;
        private float label_width;
        private float checkbox_size = 10f;

        private RectangleF checkbox_bounds;
        private RectangleF label_bounds;

        // Action
        public Action CheckedChanged { get; set; }

        public CheckboxUI(string label, bool checked_value = false,
            LabelOrientation orientation = LabelOrientation.Vertical)
        {
            Label = label;
            Checked = checked_value;
            Orientation = orientation;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            label_width = GH_FontServer.StringWidth(Label, label_font) + 8f;
            label_height = label_font.Height;

            Bounds = new RectangleF(0, 0, MinWidth(), Height());
            checkbox_bounds = new RectangleF();
            label_bounds = new RectangleF();
        }

        private class CheckboxUndoAction : IGH_UndoAction
        {
            private bool _checked;
            private CheckboxUI ui;

            public CheckboxUndoAction(bool current_check, CheckboxUI ui)
            {
                _checked = current_check;
                this.ui = ui;
            }

            public bool ExpiresSolution => false;

            public bool ExpiresDisplay => true;

            public GH_UndoState State { get; set; } = GH_UndoState.undo;

            public bool Read(GH_IReader reader)
            {
                throw new NotImplementedException();
            }

            public void Redo(GH_Document doc)
            {
                ui.Checked = !_checked;
                ui.CheckedChanged?.Invoke();
                ui.Owner.OnDisplayExpired();
                State = GH_UndoState.undo;
            }

            public void Undo(GH_Document doc)
            {
                ui.Checked = !_checked;
                ui.CheckedChanged?.Invoke();
                ui.Owner.OnDisplayExpired();
                State = GH_UndoState.redo;
            }

            public bool Write(GH_IWriter writer)
            {
                throw new NotImplementedException();
            }
        }

        public override float Height()
            => Orientation == LabelOrientation.Horizontal
                ? Margin * 2 + Math.Max(label_height, checkbox_size)
                : Margin * 3 + label_height + checkbox_size;

        public override float MinWidth()
        {
            float content_width = Orientation == LabelOrientation.Horizontal
                ? label_width + LabelSpacing + checkbox_size
                : Math.Max(label_width, checkbox_size);
            return content_width + Margin * 2;
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // ラベルの描画
                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;

                graphics.DrawString(Label, label_font, Brushes.Black, label_bounds, format);

                // チェックボックスの描画
                DrawCheckbox(graphics, checkbox_bounds, Checked);
            }
        }

        public override void UpdateLayout()
        {
            if (Orientation == LabelOrientation.Horizontal)
            {
                // ラベルとチェックボックスを横に並べて中央に配置
                float content_width = label_width + LabelSpacing + checkbox_size;
                float content_x = Bounds.X + (Bounds.Width - content_width) / 2f;
                float center_y = Bounds.Y + Bounds.Height / 2f;

                label_bounds.X = content_x;
                label_bounds.Y = center_y - label_height / 2f;
                label_bounds.Width = label_width;
                label_bounds.Height = label_height;

                checkbox_bounds.X = content_x + label_width + LabelSpacing;
                checkbox_bounds.Y = center_y - checkbox_size / 2f;
                checkbox_bounds.Width = checkbox_size;
                checkbox_bounds.Height = checkbox_size;
                return;
            }

            // ラベルの配置（上部中央）
            label_bounds.X = Bounds.X + Margin;
            label_bounds.Y = Bounds.Y + Margin;
            label_bounds.Width = Bounds.Width - Margin * 2;
            label_bounds.Height = label_height;

            // チェックボックスの配置（下部中央）
            float checkbox_x = Bounds.X + (Bounds.Width - checkbox_size) / 2f;
            float checkbox_y = Bounds.Y + Margin * 2 + label_height;
            checkbox_bounds.X = checkbox_x;
            checkbox_bounds.Y = checkbox_y;
            checkbox_bounds.Width = checkbox_size;
            checkbox_bounds.Height = checkbox_size;
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            if (checkbox_bounds.Contains(e.CanvasLocation))
            {
                Checked = !Checked;
                OnCheckedChanged();
                return new UIResponse(GH_ObjectResponse.Handled);
            }

            return base.RespondToMouseDown(sender, e);
        }

        private void OnCheckedChanged()
        {
            Owner.Owner.RecordUndoEvent("Checkbox",
                new CheckboxUndoAction(Checked, this));

            CheckedChanged?.Invoke();
        }

        private void DrawCheckbox(Graphics graphics, RectangleF bounds, bool enabled)
        {
            Pen pen = new Pen(Color.Black, 1.5f);
            Brush brush_off = Brushes.White;

            // 外枠を描画する
            graphics.FillRectangle(brush_off, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            if (enabled)
            {
                float x = bounds.X;
                float y = bounds.Y;
                float size = bounds.Width;
                Pen check_pen = new Pen(Color.Black, size / 6); // CheckBoxのライン
                // チェックを描画する
                var lines = new[] { new PointF(x + size / 4, y + size / 2),
                            new PointF(x + size / 2, y + 3 * size / 4),
                            new PointF(x + 3 * size / 4, y + size / 4) };
                graphics.DrawLines(check_pen, lines);
            }
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean(UniqueName, Checked);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            bool checked_value = false;
            if (reader.TryGetBoolean(UniqueName, ref checked_value))
            {
                Checked = checked_value;
                return true;
            }
            return false;
        }
    }
}
