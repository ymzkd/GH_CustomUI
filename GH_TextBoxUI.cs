using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using System;
using System.Drawing;

namespace GH_CustomUI
{
    /// <summary>
    /// TextBoxUI
    /// </summary>
    public class TextBoxUI : GH_UIParts
    {
        private class InputTextField : Grasshopper.GUI.Base.GH_TextBoxInputBase
        {
            private readonly TextBoxUI m_ui;

            public InputTextField(TextBoxUI ui)
            {
                m_ui = ui;
            }

            protected override void HandleTextInputAccepted(string text)
            {
                m_ui.Contents = text;
            }
        }

        public float MinTextBoxWidth { get; set; } = 80f;
        // Property
        public string Label { get; set; }

        private string m_contents = "";
        public string Contents
        {
            get { return m_contents; }
            set
            {
                if (m_contents == value) return;
                string oldValue = m_contents;
                m_contents = value;
                OnValueChanged(oldValue, value);
            }
        }

        public string Placeholder { get; set; } = "Input Here";

        public Action ValueChanged { get; set; } = () => { };
        // UI Parameter
        public float Margin { get; set; } = 2f;

        private float label_horizontal_spacing => Margin;

        public RectangleF ContentsBounds
            => RectangleF.Inflate(Bounds, -Margin, -Margin);

        // Button Status
        protected bool mouseDown, mouseOver;

        protected Font label_font = GH_FontServer.Standard;


        public TextBoxUI() { }

        public TextBoxUI(string label, string contents = "")
        {
            Label = label;
            m_contents = contents;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
        }

        public override float Height()
        {
            return 20f + Margin * 2;
        }

        public RectangleF LabelBound
        {
            get
            {
                RectangleF b = ContentsBounds;
                float d = GH_FontServer.StringWidth(Label, label_font) + label_horizontal_spacing;
                b.Width = d;
                return b;
            }
        }

        public RectangleF InputBound
        {
            get
            {
                RectangleF b = ContentsBounds;
                float d = GH_FontServer.StringWidth(Label, label_font) + label_horizontal_spacing;
                b.X += d;
                b.Width -= d;
                return b;
            }
        }

        public void OnValueChanged()
        {
            ValueChanged?.Invoke();
        }

        public void OnValueChanged(string pre, string post)
        {
            NotifyDocumentModified();

            Owner.Owner.RecordUndoEvent("TextChenged",
                new TextBoxUIUndoAction(pre, post, this));
            ValueChanged?.Invoke();
            this.Owner.OnDisplayExpired();
        }

        public override float MinWidth()
        {
            float labelwidth = GH_FontServer.StringWidth(Label, label_font);
            return 2 * Margin + 2 * label_horizontal_spacing + labelwidth + MinTextBoxWidth;
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // Label
                Brush txt_brush = new SolidBrush(Color.White);
                graphics.DrawString(Label, label_font, txt_brush, LabelBound,
                    GH_TextRenderingConstants.CenterCenter);

                Brush br = new SolidBrush(Color.White);
                graphics.FillRectangle(br, Rectangle.Round(InputBound));
                Pen pen = new Pen(Color.LightGray, 1f);
                graphics.DrawRectangle(pen, Rectangle.Round(InputBound));

                // TextBox
                if (string.IsNullOrEmpty(Contents))
                {
                    txt_brush = new SolidBrush(Color.Gray);
                    graphics.DrawString(Placeholder, label_font, txt_brush, InputBound,
                        GH_TextRenderingConstants.CenterCenter);
                }
                else
                {
                    txt_brush = new SolidBrush(Color.Black);
                    graphics.DrawString(Contents, label_font, txt_brush, InputBound,
                        GH_TextRenderingConstants.CenterCenter);
                }

            }
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            if (InputBound.Contains(e.CanvasLocation))
            {
                var matrix = sender.Viewport.XFormMatrix(GH_Viewport.GH_DisplayMatrix.CanvasToControl);
                var field = new InputTextField(this)
                {
                    Bounds = GH_Convert.ToRectangle(InputBound),
                    Font = label_font
                };

                field.ShowTextInputBox(sender, this.Contents, true, true, matrix);
                return new UIResponse(GH_ObjectResponse.Handled);
            }
            return base.RespondToMouseDown(sender, e);
        }

        public override UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (InputBound.Contains(e.CanvasLocation))
            {
                mouseOver = true;
                return new UIResponse(GH_ObjectResponse.Capture);
            }

            if (mouseOver)
            {
                mouseOver = false;
                if (mouseDown)
                    return new UIResponse(GH_ObjectResponse.Ignore);
                else
                    return new UIResponse(GH_ObjectResponse.Release);
            }
            return base.RespondToMouseMove(sender, e);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetString(UniqueName, Contents);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            string read_content = "";
            bool s = reader.TryGetString(UniqueName, ref read_content);
            if (s) Contents = read_content;
            return base.Read(reader);
        }

        private void force_text_input(string text)
        {
            //this.OnValueChanged(this.Contents, text);
            m_contents = text;
            //this.Contents = text;
        }

        private class TextBoxUIUndoAction : IGH_UndoAction
        {
            private TextBoxUI ui;
            private string previous_text;
            private string text;

            public bool ExpiresSolution => false;

            public bool ExpiresDisplay => true;

            public GH_UndoState State { get; set; } = GH_UndoState.undo;

            public TextBoxUIUndoAction() { }

            public TextBoxUIUndoAction(string pre, string post, TextBoxUI ui)
            {
                this.ui = ui;
                previous_text = pre;
                text = post;
            }

            public bool Read(GH_IReader reader)
            {
                throw new NotImplementedException();
            }

            public void Redo(GH_Document doc)
            {
                this.ui.force_text_input(text);
                this.ui.Owner.OnDisplayExpired();
                this.ui.ValueChanged?.Invoke();
                State = GH_UndoState.undo;
            }

            public void Undo(GH_Document doc)
            {
                this.ui.force_text_input(previous_text);
                this.ui.Owner.OnDisplayExpired();
                this.ui.ValueChanged?.Invoke();
                State = GH_UndoState.redo;
            }

            public bool Write(GH_IWriter writer)
            {
                throw new NotImplementedException();
            }
        } // class TextBoxUIUndoAction

    } // class TextBoxUI
}
