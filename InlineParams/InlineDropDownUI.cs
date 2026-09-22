using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// Param 1行の中に収まる高さのドロップダウン。
    /// </summary>
    /// <remarks>
    /// 選択状態は保持せず、表示は<see cref="SelectedIndexProvider"/>、
    /// 確定は<see cref="CommitIndex"/>へ委譲する。
    /// </remarks>
    public class InlineDropDownUI : GH_UIParts
    {
        private readonly Font m_font;
        private int m_focusedIndex = -1;

        public InlineDropDownUI()
        {
            Font f = GH_FontServer.Standard;
            m_font = new Font(f.FontFamily, f.Size / GH_GraphicsUtil.UiScale, f.Style);
        }

        public List<string> Items { get; set; } = new List<string>();

        public Func<int> SelectedIndexProvider { get; set; }

        public Action<int> CommitIndex { get; set; }

        public bool IsMenuExpand { get; private set; }

        public float PreferredHeight { get; set; } = 18f;

        /// <summary>ドロップダウンボタン部の幅。</summary>
        public float ButtonWidth { get; set; } = 14f;

        /// <summary>0より大きい値を設定するとその幅を希望幅として申告する。</summary>
        public float PreferredWidth { get; set; } = 0f;

        public int SelectedIndex => SelectedIndexProvider == null ? -1 : SelectedIndexProvider();

        public override float Height() => PreferredHeight;

        public override float MinWidth()
            => PreferredWidth > 0f
                ? PreferredWidth
                : MaxTextWidth(Items, m_font) + ButtonWidth + 10f;

        private RectangleF ItemBounds(int index)
            => new RectangleF(Bounds.X, Bounds.Bottom + Bounds.Height * index, Bounds.Width, Bounds.Height);

        private RectangleF MenuBounds
            => new RectangleF(Bounds.X, Bounds.Bottom, Bounds.Width, Bounds.Height * Items.Count);

        /// <summary>
        /// 描画用の書式。内容が固定なのでインスタンス間で共有する。
        /// </summary>
        private static readonly StringFormat s_format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (Bounds.Width < 1f || Bounds.Height < 1f) return;

            if (channel == GH_CanvasChannel.Objects)
            {
                RectangleF b = Bounds;

                using (SolidBrush fill = new SolidBrush(Enable ? Color.White : Color.FromArgb(226, 226, 226)))
                    graphics.FillRectangle(fill, b);

                using (Pen pen = new Pen(Enable ? Color.FromArgb(90, 90, 90) : Color.FromArgb(160, 160, 160), 1f))
                    graphics.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);

                int index = SelectedIndex;
                string txt = (index >= 0 && index < Items.Count) ? Items[index] : "---";

                using (SolidBrush tb = new SolidBrush(Enable ? Color.Black : Color.FromArgb(120, 120, 120)))
                    graphics.DrawString(txt, m_font, tb,
                        new RectangleF(b.X + 3f, b.Y, b.Width - ButtonWidth - 3f, b.Height), s_format);

                PointF centre = new PointF(b.Right - ButtonWidth * 0.5f, b.Y + b.Height * 0.5f);
                DrawDropDownButton(graphics, centre,
                    Enable ? Color.FromArgb(60, 60, 60) : Color.FromArgb(150, 150, 150),
                    (int)Math.Min(ButtonWidth, b.Height));
            }
            else if (channel == GH_CanvasChannel.Overlay && IsMenuExpand)
            {
                RectangleF menu = MenuBounds;

                using (SolidBrush fill = new SolidBrush(Color.White))
                    graphics.FillRectangle(fill, menu);

                for (int i = 0; i < Items.Count; i++)
                {
                    RectangleF ib = ItemBounds(i);
                    if (i == m_focusedIndex)
                        using (SolidBrush focus = new SolidBrush(Color.FromArgb(210, 220, 235)))
                            graphics.FillRectangle(focus, ib);

                    using (SolidBrush tb = new SolidBrush(Color.Black))
                        graphics.DrawString(Items[i], m_font, tb,
                            new RectangleF(ib.X + 3f, ib.Y, ib.Width - 3f, ib.Height), s_format);
                }

                using (Pen pen = new Pen(Color.FromArgb(90, 90, 90), 1f))
                    graphics.DrawRectangle(pen, menu.X, menu.Y, menu.Width, menu.Height);
            }
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!Enable || e.Button != MouseButtons.Left) return UIResponse.Ignore;

            if (Bounds.Contains(e.CanvasLocation))
            {
                IsMenuExpand = !IsMenuExpand;
                m_focusedIndex = -1;
                return new UIResponse(
                    IsMenuExpand ? GH_ObjectResponse.Capture : GH_ObjectResponse.Release, true);
            }

            if (!IsMenuExpand) return UIResponse.Ignore;

            for (int i = 0; i < Items.Count; i++)
            {
                if (!ItemBounds(i).Contains(e.CanvasLocation)) continue;

                IsMenuExpand = false;
                m_focusedIndex = -1;
                if (i != SelectedIndex) CommitIndex?.Invoke(i);
                return new UIResponse(GH_ObjectResponse.Release, true);
            }

            // メニュー外をクリックしたら閉じるだけ
            IsMenuExpand = false;
            m_focusedIndex = -1;
            return new UIResponse(GH_ObjectResponse.Release, true);
        }

        public override UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!IsMenuExpand) return UIResponse.Ignore;

            for (int i = 0; i < Items.Count; i++)
            {
                if (!ItemBounds(i).Contains(e.CanvasLocation)) continue;
                if (m_focusedIndex == i) return new UIResponse(GH_ObjectResponse.Handled, false);

                m_focusedIndex = i;
                return new UIResponse(GH_ObjectResponse.Handled, true);
            }

            if (m_focusedIndex < 0) return UIResponse.Ignore;

            m_focusedIndex = -1;
            return new UIResponse(GH_ObjectResponse.Handled, true);
        }

        public override UIResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
        {
            if (!IsMenuExpand || e.KeyCode != Keys.Escape) return UIResponse.Ignore;

            IsMenuExpand = false;
            m_focusedIndex = -1;
            return new UIResponse(GH_ObjectResponse.Release, true);
        }
    } // class InlineDropDownUI
}
