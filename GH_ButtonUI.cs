using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace GH_CustomUI
{
    public enum ButtonState
    {
        Normal, Hover, Down
    }

    /// <summary>
    /// ButtonUIBase
    /// </summary>
    abstract public class ButtonUIBase : GH_UIParts
    {
        public Bitmap Icon { get; set; } = null;
        public Bitmap DownIcon { get; set; } = null;
        // Property
        public string Label { get; set; }

        public abstract ButtonState ButtonState { get; }

        public ButtonColourTheme ColourTheme { get; set; } = new ButtonColourTheme();

        public Action OnClicked { get; set; } = () => { };
        // UI Parameter
        public float ButtonHeight { get; set; } = 20f;
        public float Margin { get; set; } = 2f;
        public float RoundRadious { get; set; } = 3f;
        // Button Status
        protected bool mouseDown, mouseOver;

        protected bool IsRenderIcon => Icon != null && string.IsNullOrEmpty(Label);

        protected Font label_font { get; set; } = GH_FontServer.Standard;

        protected RectangleF buttonBounds
        {
            get { return RectangleF.Inflate(Bounds, -Margin, -Margin); }
        }

        public ButtonUIBase() { }

        public ButtonUIBase(Bitmap icon)
        {
            Icon = icon;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
        }

        public ButtonUIBase(string label)
        {
            Label = label;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
        }

        public override float Height()
        {
            return ButtonHeight;
        }

        public override float MinWidth()
        {
            if (IsRenderIcon)
                return 2 * (RoundRadious + Margin) + Height();
            else
            {
                float space = GH_FontServer.StringWidth(Label, label_font);
                return 2 * (RoundRadious + Margin) + space;
            }
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // Botton Bound
                RectangleF bbound = buttonBounds;
                GraphicsPath gp = RoundedRect(bbound, (int)RoundRadious);

                // Button Body fill
                Brush brush;
                if (ButtonState == ButtonState.Down)
                    brush = ColourTheme.ClickedButtonBrush;
                else if (ButtonState == ButtonState.Hover)
                    brush = ColourTheme.HoverButtonBrush;
                else
                    brush = ColourTheme.ButtonBrush;
                graphics.FillPath(brush, gp);

                // Button Body path
                Pen pen;
                if (ButtonState == ButtonState.Down) pen = new Pen(ColourTheme.ClickedBorderColor);
                else if (ButtonState == ButtonState.Hover) pen = new Pen(ColourTheme.HoverBorderColor);
                else pen = new Pen(ColourTheme.BorderColor);
                graphics.DrawPath(pen, gp);

                // Button Overlay
                GraphicsPath overlay = RoundedRect(buttonBounds, 2, overlay: true);
                graphics.FillPath(
                    new SolidBrush(Color.FromArgb(60, 255, 255, 255)), overlay);

                // Button Contents
                if (IsRenderIcon)
                {
                    bool swap_icon = (ButtonState == ButtonState.Down) && DownIcon != null;
                    graphics.DrawImage(swap_icon ? DownIcon : Icon,
                            buttonBounds.X + buttonBounds.Width / 2 - Height() / 2 + Margin,
                            buttonBounds.Y + Margin / 2,
                            Height() - 2 * Margin, Height() - 2 * Margin);

                    //graphics.DrawImage(swap_icon ? DownIcon : Icon,
                    //    buttonBounds.X + buttonBounds.Width / 2 - Height() / 2 + (RoundRadious + Margin),
                    //    buttonBounds.Y + (RoundRadious + Margin),
                    //    Height() - 2 * (RoundRadious + Margin),
                    //    Height() - 2 * (RoundRadious + Margin));
                }
                else
                {
                    Brush txt_brush = new SolidBrush(Color.White);
                    graphics.DrawString(Label, label_font, txt_brush, Bounds,
                        GH_TextRenderingConstants.CenterCenter);
                }
            }
        }

        public override abstract UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e);

        public override abstract UIResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e);

        public override UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (buttonBounds.Contains(e.CanvasLocation))
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

            return UIResponse.Ignore;
        }
    }

    /// <summary>
    /// ButtonUI
    /// </summary>
    public class ButtonUI : ButtonUIBase
    {
        public override ButtonState ButtonState =>
            mouseDown ? ButtonState.Down : (mouseOver ? ButtonState.Hover : ButtonState.Normal);

        public ButtonUI() : base() { }

        public ButtonUI(Bitmap icon) : base(icon) { }

        public ButtonUI(string label) : base(label) { }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            if (buttonBounds.Contains(e.CanvasLocation))
            {
                mouseDown = true;
                return new UIResponse(GH_ObjectResponse.Capture);
            }

            return UIResponse.Ignore;
        }

        public override UIResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            if (mouseDown)
            {
                if (buttonBounds.Contains(e.CanvasLocation))
                    OnClicked?.Invoke();

                mouseDown = false;
                return new UIResponse(GH_ObjectResponse.Release);
            }

            return UIResponse.Ignore;
        }
    }


    /// <summary>
    /// ボタンのUIでオンオフの状態を切り替え出来るボタン
    /// </summary>
    public class ToggleButtonUI : ButtonUIBase
    {
        public override ButtonState ButtonState =>
            (mouseDown || Checked) ? ButtonState.Down : (mouseOver ? ButtonState.Hover : ButtonState.Normal);

        public ButtonGroupSelectionType SelectionType { get; set; }

        public bool Checked { get; set; } = false;

        public ToggleButtonUI() : base()
        {
            ColourTheme.Primary = Color.Gray;
        }

        public ToggleButtonUI(Bitmap icon, bool button_checked = false) : base(icon)
        {
            ButtonHeight = 25f;
            RoundRadious = 0.5f;
            ColourTheme.Primary = Color.Gray;
            Checked = button_checked;
        }

        public ToggleButtonUI(string label, bool button_checked=false) : base(label)
        {
            ColourTheme.Primary = Color.Gray;
            Checked = button_checked;
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            if (buttonBounds.Contains(e.CanvasLocation))
            {
                // すでにTrueのRadioButtonはもう一回は押せない。
                if (SelectionType == ButtonGroupSelectionType.RadioGroup && Checked)
                    return new UIResponse(GH_ObjectResponse.Ignore);

                mouseDown = true;
                return new UIResponse(GH_ObjectResponse.Capture);
            }

            return UIResponse.Ignore;
        }

        public override UIResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 左クリック以外は無視
            if (e.Button != System.Windows.Forms.MouseButtons.Left)
                return UIResponse.Ignore;

            if (mouseDown)
            {
                if (buttonBounds.Contains(e.CanvasLocation))
                {
                    OnClicked?.Invoke();
                    Checked = !Checked;
                }

                mouseDown = false;
                return new UIResponse(GH_ObjectResponse.Release);
            }

            return UIResponse.Ignore;
        }
    }



    public class ButtonColourTheme
    {
        //Set colours for Component UI
        readonly static Color PrimaryDefault = Color.FromArgb(255, 229, 27, 36);
        public Color Primary { get; set; } = Color.FromArgb(255, 64, 0, 255);
        public Color Primary_light => WhiteOverlay(Primary, 0.32);
        // { get; set; } = WhiteOverlay(PrimaryDefault, 0.32);
        public Color Primary_dark => Overlay(Primary, Color.Black, 0.32);
        // { get; set; } = Overlay(PrimaryDefault, Color.Black, 0.32);

        public ButtonColourTheme() { }
        public ButtonColourTheme(Color primary)
        {
            Primary = primary;
        }

        public Brush ButtonBrush
        {
            get { return new SolidBrush(Primary); }
        }
        public Brush ClickedButtonBrush
        {
            get { return new SolidBrush(Primary_light); }
        }
        public Brush HoverButtonBrush
        {
            get { return new SolidBrush(Overlay(Primary, Color.Black, 0.15)); }
            //get { return new SolidBrush(Overlay(Primary, Color.Black, 0.04)); }
        }
        public Color BorderColor
        {
            get { return Primary_dark; }
        }
        public Color ClickedBorderColor
        {
            get { return Primary; }
        }
        public Color HoverBorderColor
        {
            get { return WhiteOverlay(Primary, 0.86); }
        }
        public static Color WhiteOverlay(Color original, double ratio)
        {
            Color white = Color.White;
            return Color.FromArgb(255,
                (int)(ratio * white.R + (1 - ratio) * original.R),
                (int)(ratio * white.G + (1 - ratio) * original.G),
                (int)(ratio * white.B + (1 - ratio) * original.B));
        }
        public static Color Overlay(Color original, Color overlay, double ratio)
        {
            return Color.FromArgb(255,
                (int)(ratio * overlay.R + (1 - ratio) * original.R),
                (int)(ratio * overlay.G + (1 - ratio) * original.G),
                (int)(ratio * overlay.B + (1 - ratio) * original.B));
        }

    }
}
