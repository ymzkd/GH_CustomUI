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
    /// 実装中
    /// </summary>
    public class SliderUI : GH_UIParts
    {
        private class InputNumberField : Grasshopper.GUI.Base.GH_TextBoxInputBase
        {
            private readonly SliderUI m_ui;

            public InputNumberField(SliderUI ui)
            {
                m_ui = ui;
            }

            protected override void HandleTextInputAccepted(string text)
            {
                if (GH_Convert.ToDouble(text, out double value, GH_Conversion.Both))
                {
                    m_ui.Value = value;
                    m_ui.record_value();
                }

            }
        }

        private class SliderUIUndoAction : IGH_UndoAction
        {
            private SliderUI ui;
            private double preview_value, current_value;

            public SliderUIUndoAction(SliderUI ui,
                double preview_value, double current_value)
            {
                this.ui = ui;
                this.preview_value = preview_value;
                this.current_value = current_value;
            }

            public bool ExpiresSolution => false;

            public bool ExpiresDisplay => true;

            public GH_UndoState State { get; set; } = GH_UndoState.undo;

            public bool Read(GH_IReader reader)
                => throw new NotImplementedException();

            public void Redo(GH_Document doc)
            {
                ui.Value = current_value;

                // Undoを実行したので次に動作するとしたらRedo
                State = GH_UndoState.undo;
            }

            public void Undo(GH_Document doc)
            {
                ui.Value = preview_value;

                // Undoを実行したので次に動作するとしたらRedo
                State = GH_UndoState.redo;
            }

            public bool Write(GH_IWriter writer)
                => throw new NotImplementedException();
        }

        public double Value { get; set; } = 0;
        private double recorded_value = 0;

        public double MinValue { get; set; } = 0;
        public double MaxValue { get; set; } = 100;

        private float MinSliderX => SliderBound.X + ThumbDiameter / 2;
        private float MaxSliderX => SliderBound.Right - ThumbDiameter / 2;

        private double ThumbRate
        {
            get
            {
                double dragPercentage = (Value - MinValue) / (MaxValue - MinValue);
                if (dragPercentage < 0) return 0;
                else if (dragPercentage > 1) return 1;
                else return dragPercentage;
            }
        }

        private float ThumbX
        {
            get
            {
                return (float)(SliderBound.X + ThumbDiameter / 2 + (SliderBound.Width - ThumbDiameter) * ThumbRate);
            }
        }

        // Property
        public Bitmap Icon { get; set; } = null;
        public string Label { get; set; }

        public float ThumbDiameter { get; set; } = 8f;
        // UI Parameter
        public float Margin = 2f;

        private float label_horizontal_spacing => Margin;
        public float MinSliderWidth { get; set; } = 50f;
        // Button Status
        protected bool mouseDown, mouseOver;

        protected bool IsRenderIcon => Icon != null && string.IsNullOrEmpty(Label);

        protected Font label_font = GH_FontServer.Standard;

        public event EventHandler ValueChange;
        public event EventHandler ValueRecorded;

        public SliderUI() { }

        public SliderUI(Bitmap icon)
        {
            Icon = icon;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
        }

        public SliderUI(string label)
        {
            Label = label;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
        }

        public override float Height()
        {
            return 20f;
        }

        public override float MinWidth()
        {
            float slider_width = MinSliderWidth;
            if (IsRenderIcon)
            {
                float label_width = 2 * label_horizontal_spacing + Height();

                return label_width + 2 * Margin + slider_width;
            }
            else
            {
                float label_width = GH_FontServer.StringWidth(Label, label_font) + 2 * label_horizontal_spacing;
                return 2 * Margin + label_width + slider_width;
            }
        }
        public RectangleF ContentsBounds
            => RectangleF.Inflate(Bounds, -Margin, -Margin);

        public RectangleF LabelBound
        {
            get
            {
                RectangleF b = ContentsBounds;
                //PointF org = Bounds.Location;
                float d = GH_FontServer.StringWidth(Label, label_font) + label_horizontal_spacing;
                b.Width = d;
                return b;
            }
        }

        public RectangleF SliderBound
        {
            get
            {
                RectangleF b = ContentsBounds;
                //PointF org = Bounds.Location;
                float d = GH_FontServer.StringWidth(Label, label_font) + label_horizontal_spacing;
                b.X += d;
                b.Width -= d;
                return b;
            }
        }

        public RectangleF ThumbBound
        {
            get
            {
                return new RectangleF(ThumbX - ThumbDiameter / 2,
                    SliderBound.Y + SliderBound.Height / 2 - ThumbDiameter / 2,
                    ThumbDiameter, ThumbDiameter);
            }
        }

        public RectangleF SliderPlotBound
        {
            get
            {
                var thumbBound = ThumbBound;
                var sliderBound = SliderBound;
                if (ThumbRate < 0.5) // Plot in right side.
                    return new RectangleF(thumbBound.Right, sliderBound.Y, sliderBound.Right - thumbBound.Right, sliderBound.Height);
                else // Plot in left side.
                    return new RectangleF(sliderBound.X, sliderBound.Y, thumbBound.X - sliderBound.X, sliderBound.Height);
            }
        }

        private void record_value()
        {
            if (recorded_value == Value) return;

            Owner.Owner.RecordUndoEvent("Slider",
                new SliderUIUndoAction(this, recorded_value, Value));

            OnValueRecoreded();
            recorded_value = Value;
        }

        public void OnValueChanged()
        {
            ValueChange?.Invoke(this, EventArgs.Empty);
        }

        public void OnValueRecoreded()
        {
            ValueRecorded?.Invoke(this, EventArgs.Empty);
            OnValueChanged();
        }

        public override void UpdateLayout()
        {

            if (mouseDown)
            {
                double prevalue = Value;
                float mouse_posx = mouse_drag_deltaX + mouse_drag_pivotX;
                if (mouse_posx >= MinSliderX)
                {
                    if (mouse_posx <= MaxSliderX) // between min & max
                    {
                        float LSum = SliderBound.Width - ThumbDiameter;
                        float LRef = mouse_posx - MinSliderX;
                        Value = MinValue + LRef / LSum * (MaxValue - MinValue);
                    }
                    else // overflow
                    {
                        Value = MaxValue;
                        mouse_drag_deltaX = MaxSliderX - mouse_drag_pivotX;
                    }
                }
                else // underflow
                {
                    Value = MinValue;
                    mouse_drag_deltaX = MinSliderX - mouse_drag_pivotX;
                }

                if (prevalue != Value)
                    OnValueChanged();
            }

            base.UpdateLayout();
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // Label
                Brush txt_brush = new SolidBrush(Color.Red);
                graphics.DrawString(Label, label_font, txt_brush, LabelBound,
                    GH_TextRenderingConstants.CenterCenter);

                // Slider Line
                Pen linepen = new Pen(Color.LightGray, 2f);
                graphics.DrawLine(linepen,
                    new PointF(SliderBound.X + ThumbDiameter / 2, SliderBound.Y + SliderBound.Height / 2),
                    new PointF(SliderBound.Right - ThumbDiameter / 2, SliderBound.Y + SliderBound.Height / 2));

                // Thumb Icon
                Pen pen = new Pen(Color.Black);
                pen.Width = 2f;
                RectangleF button = ThumbBound;

                Brush fill = new SolidBrush(Color.White);
                graphics.FillEllipse(fill, button);
                graphics.DrawEllipse(pen, button);

                // Draw display value text
                Font font = new Font(GH_FontServer.FamilyStandard, 7);
                // adjust fontsize to high resolution displays
                font = new Font(font.FontFamily, font.Size / GH_GraphicsUtil.UiScale, FontStyle.Regular);
                string val = string.Format("{0:F}", new decimal(Value));
                Brush br = new SolidBrush(Color.Black);
                graphics.DrawString(val, font, br, SliderPlotBound,
                    (ThumbRate < 0.5) ?
                    GH_TextRenderingConstants.NearCenter : GH_TextRenderingConstants.FarCenter);

            }
        }

        float mouse_drag_pivotX = 0;
        float mouse_drag_deltaX = 0;

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            System.Drawing.RectangleF rec = ThumbBound;
            if (rec.Contains(e.CanvasLocation))
            {
                mouse_drag_pivotX = e.CanvasLocation.X;
                mouse_drag_deltaX = 0;
                mouseDown = true;
                return new UIResponse(GH_ObjectResponse.Capture);
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override UIResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            if (mouseDown)
            {
                UpdateLayout(); // Update before switching mouseDown

                record_value();

                mouseDown = false;
                return new UIResponse(GH_ObjectResponse.Release);
            }
            return base.RespondToMouseUp(sender, e);
        }

        public override UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (mouseDown)
            {
                mouse_drag_deltaX = e.CanvasLocation.X - mouse_drag_pivotX;
                Grasshopper.Instances.CursorServer.AttachCursor(sender, "GH_NumericSlider");
                return UIResponse.Handled;
            }

            RectangleF rec = ThumbBound;
            if (rec.Contains(e.CanvasLocation))
            {
                mouseOver = true;
                Grasshopper.Instances.CursorServer.AttachCursor(sender, "GH_NumericSlider");
                return new UIResponse(GH_ObjectResponse.Capture);
            }

            if (mouseOver)
            {
                mouseOver = false;
                Grasshopper.Instances.CursorServer.ResetCursor(sender);
                return new UIResponse(GH_ObjectResponse.Release);
            }
            return base.RespondToMouseMove(sender, e);
        }

        public override UIResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            RectangleF rec = LabelBound;
            if (rec.Contains(e.CanvasLocation))
            {

                Grasshopper.Kernel.Special.GH_NumberSlider dummy_slider = new Grasshopper.Kernel.Special.GH_NumberSlider();
                dummy_slider.Slider.Maximum = (decimal)MaxValue;
                dummy_slider.Slider.Minimum = (decimal)MinValue;

                dummy_slider.Slider.Value = (decimal)Value;
                Grasshopper.GUI.GH_NumberSliderPopup gH_MenuSliderForm = new Grasshopper.GUI.GH_NumberSliderPopup();
                GH_WindowsFormUtil.CenterFormOnCursor(gH_MenuSliderForm, true);
                gH_MenuSliderForm.Setup(dummy_slider);

                var res = gH_MenuSliderForm.ShowDialog();
                if (res == DialogResult.OK)
                {
                    MaxValue = (double)dummy_slider.Slider.Maximum;
                    MinValue = (double)dummy_slider.Slider.Minimum;
                    Value = (double)dummy_slider.Slider.Value;

                    record_value();
                    return UIResponse.Handled;
                }
            }
            else if (SliderBound.Contains(e.CanvasLocation))
            {
                var matrix = sender.Viewport.XFormMatrix(GH_Viewport.GH_DisplayMatrix.CanvasToControl);
                var field = new InputNumberField(this)
                {
                    Bounds = GH_Convert.ToRectangle(SliderBound),
                    Font = label_font
                };

                field.ShowTextInputBox(sender, this.Value.ToString(), true, true, matrix);
                return UIResponse.Handled;
            }

            return base.RespondToMouseDoubleClick(sender, e);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetDouble(UniqueName, Value);
            writer.SetDouble(UniqueName + "Min", MinValue);
            writer.SetDouble(UniqueName + "Max", MaxValue);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            double value = 0;
            double minvalue = 0;
            double maxvalue = 0;
            reader.TryGetDouble(UniqueName, ref value);
            reader.TryGetDouble(UniqueName + "Min", ref minvalue);
            reader.TryGetDouble(UniqueName + "Max", ref maxvalue);

            Value = value;
            MinValue = minvalue;
            MaxValue = maxvalue;
            return base.Read(reader);
        }
    } // SliderUI
}
