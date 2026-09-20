using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System.Drawing;

namespace GH_CustomUI
{
    /// <summary>
    /// 水平分割線とラベルのGHUI
    /// </summary>
    public class SeparatorLabelUI : GH_UIParts
    {
        public string Label;
        public float Margin = 2f;
        private Font labelFont = GH_FontServer.Standard;

        public SeparatorLabelUI(string label)
        {
            Label = label;
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
            labelFont = new Font(labelFont.FontFamily,
                labelFont.Size / GH_GraphicsUtil.UiScale, labelFont.Style);
        }

        public override float Height()
        {
            return 2f * Margin + labelFont.Height;
        }

        public override float MinWidth()
        {
            float space = GH_FontServer.StringWidth(Label, labelFont);
            return space + Margin * 2;
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                Pen spacer = new Pen(Color.Gray);
                Brush brush = new SolidBrush(Color.Black);
                graphics.DrawString(Label, labelFont, brush, Bounds, GH_TextRenderingConstants.CenterCenter);

                float lineY = Bounds.Y + Height() * 0.5f;
                float text_width = GH_FontServer.StringWidth(Label, labelFont);
                float lineLength = (Bounds.Width - 4 * Margin - text_width) * 0.5f;
                // Left Line
                graphics.DrawLine(spacer, Bounds.X + Margin, lineY, Bounds.X + Margin + lineLength, lineY);
                // Right Line
                graphics.DrawLine(spacer, Bounds.X + Bounds.Width - Margin, lineY, Bounds.X + Bounds.Width - Margin - lineLength, lineY);
            }
        }
    }
}
