using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// ToggleによりOn/Offを切り替えることが出来るスイッチUI
    /// </summary>
    public class SwitchButtonUI : GH_UIParts
    {
        public bool Value { get; set; } = true;
        public string Label;

        public float Margin = 2f;
        private Font label_font = GH_FontServer.Standard;

        private float label_height;
        private float label_bound_width;

        private float button_size = 12f;
        private float button_width => button_size * 2f;
        private RectangleF button_bound
        {
            get
            {
                float x = Bounds.Right - (Margin * 2 + label_bound_width);
                float y = Bounds.Location.Y + Margin;
                return new RectangleF(x, y, button_width, button_size);
            }
        }

        // Action
        public Action ValueChanged { get; set; }

        public SwitchButtonUI(string label, bool value = false)
        {
            Label = label;
            Value = value;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            label_bound_width = GH_FontServer.StringWidth(Label, label_font);
            label_height = label_font.Height;

            Bounds = new RectangleF(0, 0, MinWidth(), Height());

        }

        private class SwitchButtonAction : IGH_UndoAction
        {
            private bool _checked;
            private SwitchButtonUI ui;

            public SwitchButtonAction(bool current_check, SwitchButtonUI ui)
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
                ui.Value = !_checked;
                State = GH_UndoState.undo;
            }

            public void Undo(GH_Document doc)
            {
                ui.Value = !_checked;
                State = GH_UndoState.redo;
            }

            public bool Write(GH_IWriter writer)
            {
                throw new NotImplementedException();
            }
        }

        public override float Height()
            => Margin * 2 + Math.Max(label_height, button_size);

        public override float MinWidth()
        {
            float txt_width = GH_FontServer.StringWidth(Label, label_font);
            return txt_width + button_width + Margin * 3;
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // ラベルの描画
                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Near;
                format.LineAlignment = StringAlignment.Near;
                graphics.DrawString(Label, label_font, Brushes.Black, Bounds.Location, format);


                RectangleF rc = button_bound;
                GraphicsPath capsul = CapsulePath(rc);
                Brush FillBrush = new SolidBrush(Color.FromArgb(67, 113, 213));
                graphics.FillPath((Value ? FillBrush : Brushes.LightGray), capsul);

                RectangleF rc2 = rc;
                rc2.Location = new PointF(rc2.Location.X + (Value ? button_size : 0), rc2.Location.Y);
                rc2.Width = button_size;
                float button_offset = 1f;
                rc2.Inflate(-button_offset, -button_offset);
                graphics.FillEllipse(Brushes.White, rc2);
            }
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            if (button_bound.Contains(e.CanvasLocation))
            {
                Value = !Value;
                OnValueChanged();
                return new UIResponse(GH_ObjectResponse.Handled);
            }

            return base.RespondToMouseDown(sender, e);
        }

        private void OnValueChanged()
        {
            NotifyDocumentModified();

            Owner.Owner.RecordUndoEvent("Checked",
                new SwitchButtonAction(Value, this));

            ValueChanged?.Invoke();
            Owner.OnDisplayExpired();
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean(UniqueName, Value);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            bool checked_value = true;
            if (reader.TryGetBoolean(UniqueName, ref checked_value))
            {
                Value = checked_value;
                return true;
            }
            return false;
        }
    }
}
