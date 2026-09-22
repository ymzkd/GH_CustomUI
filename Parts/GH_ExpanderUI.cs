using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// 複数のUIパーツを含むコンテナで折りたたんだり展開したりできるUI
    /// </summary>
    public class ExpanderUI : GH_UIParts, IGH_TooltipAwareObject, IContainerUI
    {
        private bool mouseOver = false;

        public Brush BackGroudFillBrush { get; set; } = Brushes.LightGray;

        #region Layout Parameters
        private float BarHeight => labelFont.Height + Margin;
        private RectangleF BarBounds
            => new RectangleF(Bounds.Location, new SizeF(Bounds.Width, BarHeight));

        public RectangleF ContentBounds
            => new RectangleF(new PointF(Bounds.Location.X + Margin, Bounds.Location.Y + BarHeight),
                new SizeF(Bounds.Width - Margin * 2, Bounds.Height - BarHeight - Margin));

        private float ExpandIconSize => 22f;

        public float Margin = 2f;
        private float RoundedCornerRadius => Margin;
        #endregion

        public string Label;
        private Font labelFont = GH_FontServer.Standard;

        public List<GH_UIParts> UIParts { get; set; } = new List<GH_UIParts>();

        public bool Expanded { get; set; }

        private IPartsOwner m_owner = null;
        public override IPartsOwner Owner
        {
            get => m_owner;
            set
            {
                m_owner = value;
                foreach (var p in UIParts)
                    p.Owner = value;
            }
        }

        public override bool TooltipEnabled => true;

        public Orientation Orientation => Orientation.Vertical;

        public ExpanderUI(string label, bool expand = true)
        {
            Label = label;
            Expanded = expand;

            Bounds = new RectangleF(0, 0, MinWidth(), Height());
            labelFont = new Font(labelFont.FontFamily,
                labelFont.Size / GH_GraphicsUtil.UiScale, labelFont.Style);
        }

        public void AddUI(GH_UIParts ui)
        {
            // Ownerがnullの場合は、例外をthrow
            if (Owner == null)
                throw new InvalidOperationException("ComponentUI must have an owner.");

            ui.Owner = Owner;

            // Serialize時の名前に使う登録順。
            // このExpander専用のチャンクに書くため、
            // 一意であればよいのはExpanderの中だけとなる
            if (ui.Index < 0) ui.Index = UIParts.Count;

            UIParts.Add(ui);
        }

        public override float Height()
        {
            float base_height = 2f * Margin + labelFont.Height;
            float base_height2 = ExpandIconSize + 2f * Margin;
            float parts_height = UIParts.Where(x => x.Enable).Sum(x => x.Height());
            if (Expanded)
                return Math.Max(base_height, base_height2) + parts_height;
            else
                return Math.Max(base_height, base_height2);
        }

        public override float MinWidth()
        {
            float expander_width = GH_FontServer.StringWidth(Label, labelFont) + Margin * 4 + ExpandIconSize;
            float maxWidth = UIParts.Any() ? UIParts.Max(x => x.MinWidth() + Margin * 2) : 0;
            return Math.Max(expander_width, maxWidth);
        }

        private void OnExpanderToggle()
        {
            // 開閉状態も保存の対象なので変更として扱う
            NotifyDocumentModified();
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                RectangleF background_bound = Bounds;
                background_bound.Inflate(-Margin, -Margin);
                graphics.FillRectangle(BackGroudFillBrush, background_bound);

                float curX = 0;
                Pen spacer = new Pen(Color.Gray);
                Brush brush = new SolidBrush(Color.Black);

                var TextBound = BarBounds; TextBound.Inflate(-Margin, -Margin);
                graphics.DrawString(Label, labelFont, brush, TextBound, GH_TextRenderingConstants.NearCenter);

                float lineY = Bounds.Y + BarHeight * 0.5f;
                float text_width = GH_FontServer.StringWidth(Label, labelFont);
                float lineLength = (TextBound.Width - 2 * Margin - text_width - BarHeight);
                if (mouseOver)
                {
                    RectangleF bar_fillbounds = RectangleF.Inflate(BarBounds, -Margin, -Margin);
                    Brush brush1 = new SolidBrush(Color.FromArgb(150, 128, 128, 128));
                    graphics.FillRectangle(brush1, bar_fillbounds);
                }

                // Line
                curX = TextBound.X + text_width + Margin;
                graphics.DrawLine(spacer, curX, lineY, curX + lineLength, lineY);
                // Expander Icon
                curX += lineLength;
                DrawDropDownButton(graphics, new PointF(curX + Margin + ExpandIconSize / 2, lineY),
                    Color.Red, (int)(ExpandIconSize), Expanded);
            }

            if (Expanded)
            {
                foreach (var ui in UIParts)
                    ui.Render(canvas, graphics, channel);
            }

        }

        public override void UpdateLayout()
        {
            base.UpdateLayout();

            var base_pos = ContentBounds.Location;
            var base_width = ContentBounds.Width;
            foreach (var ui in UIParts)
            {
                ui.Bounds = new RectangleF(base_pos, new SizeF(base_width, ui.Height()));
                base_pos.Y += ui.Height();
                ui.UpdateLayout();
            }
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            UIResponse response = UIResponse.Ignore;
            if (ActiveObject != null)
            {
                response = ActiveObject.RespondToMouseDown(sender, e);

                if (response.Response == GH_ObjectResponse.Release)
                    ActiveObject = null;
            }
            else
            {
                if (BarBounds.Contains(e.CanvasLocation))
                {
                    if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    {
                        Expanded = !Expanded;
                        OnExpanderToggle();
                    }
                    return new UIResponse(GH_ObjectResponse.Handled);
                }

                foreach (GH_UIParts ui in UIParts)
                {
                    response = ui.RespondToMouseDown(sender, e);
                    if (response.Response == GH_ObjectResponse.Ignore)
                        continue;
                    else if (response.Response == GH_ObjectResponse.Capture)
                        ActiveObject = ui;

                    // 操作終了(Ignore以外)
                    break;
                }
            }

            return response;
        }

        public override UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            UIResponse response = UIResponse.Ignore;
            if (ActiveObject != null)
            {
                response = ActiveObject.RespondToMouseMove(sender, e);

                if (response.Response == GH_ObjectResponse.Release)
                    ActiveObject = null;
            }
            else
            {
                if (BarBounds.Contains(e.CanvasLocation))
                {
                    mouseOver = true;
                    return new UIResponse(GH_ObjectResponse.Capture);
                }
                if (mouseOver)
                {
                    mouseOver = false;
                    return new UIResponse(GH_ObjectResponse.Release);
                }

                foreach (GH_UIParts ui in UIParts)
                {
                    response = ui.RespondToMouseMove(sender, e);
                    if (response.Response == GH_ObjectResponse.Ignore)
                        continue;
                    else if (response.Response == GH_ObjectResponse.Capture)
                        ActiveObject = ui;

                    // 操作終了(Ignore以外)
                    break;
                }
            }

            return response;
        }

        public override UIResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            UIResponse response = UIResponse.Ignore;
            if (ActiveObject != null)
            {
                response = ActiveObject.RespondToMouseUp(sender, e);

                if (response.Response == GH_ObjectResponse.Release)
                    ActiveObject = null;
            }
            else
            {
                foreach (GH_UIParts ui in UIParts)
                {
                    response = ui.RespondToMouseUp(sender, e);
                    if (response.Response == GH_ObjectResponse.Ignore)
                        continue;
                    else if (response.Response == GH_ObjectResponse.Capture)
                        ActiveObject = ui;

                    // 操作終了(Ignore以外)
                    break;
                }
            }

            return response;
        }

        public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            base.SetupTooltip(canvasPoint, e);

            foreach (GH_UIParts ui in UIParts)
                ui.SetupTooltip(canvasPoint, e);
        }

        public override bool Write(GH_IWriter writer)
        {
            // 子と開閉状態は専用のチャンクに書き出す。
            // 親と同じ名前空間に置くと、入れ子のパーツどうしで名前が衝突する
            GH_IWriter sub = ChunkWriter(writer);
            foreach (GH_UIParts ui in UIParts)
            {
                try { ui.Write(sub); }
                catch (Exception) { }
            }

            sub.SetBoolean("Expanded", Expanded);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            GH_IReader sub = ChunkReader(reader);
            if (sub == null) return false;

            foreach (GH_UIParts ui in UIParts)
            {
                try { ui.Read(sub); }
                catch (Exception) { }
            }

            bool bool_ref = Expanded;
            if (sub.TryGetBoolean("Expanded", ref bool_ref))
                Expanded = bool_ref;

            return true;
        }

        /// <summary>子を書き出すチャンクを作る</summary>
        private GH_IWriter ChunkWriter(GH_IWriter writer)
            => Index < 0
                ? writer.CreateChunk(GetType().Name)
                : writer.CreateChunk(GetType().Name, Index);

        /// <summary>子を読み込むチャンクを取得する。無ければnull</summary>
        private GH_IReader ChunkReader(GH_IReader reader)
        {
            string name = GetType().Name;
            if (Index < 0)
                return reader.ChunkExists(name) ? reader.FindChunk(name) : null;

            return reader.ChunkExists(name, Index) ? reader.FindChunk(name, Index) : null;
        }
    }
}
