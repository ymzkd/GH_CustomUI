using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System.Drawing;

namespace GH_CustomUI
{

    /// <summary>
    /// ラベルのGHUI
    /// </summary>
    public class LabelUI : GH_UIParts
    {
        public string Label { get; set; }
        public float Margin { get; set; } = 2f;
        public Color TextColor { get; set; } = Color.Black;
        public Font LabelFont { get; set; } = GH_FontServer.Standard;

        public LabelUI(string label)
        {
            Label = label;
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
            LabelFont = new Font(LabelFont.FontFamily,
                LabelFont.Size / GH_GraphicsUtil.UiScale, LabelFont.Style);
        }

        public override float Height()
        {
            return 2f * Margin + LabelFont.Height;
        }

        public override float MinWidth()
        {
            float space = GH_FontServer.StringWidth(Label, LabelFont);
            return space + Margin * 2;
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                Brush brush = new SolidBrush(TextColor);
                graphics.DrawString(Label, LabelFont, brush, Bounds, GH_TextRenderingConstants.CenterCenter);
            }
        }
    }
}
