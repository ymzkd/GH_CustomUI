using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// Param 1行の中に収まる、オンオフを黒丸の有無で表す切り替え。
    /// </summary>
    /// <remarks>
    /// チェックマークではなくラジオボタンの見た目にしている。
    /// 状態は保持せず、表示は<see cref="ValueProvider"/>、
    /// 確定は<see cref="CommitValue"/>へ委譲する。
    /// </remarks>
    public class InlineToggleUI : GH_UIParts
    {
        /// <summary>現在のオンオフを返すデリゲート。</summary>
        public Func<bool> ValueProvider { get; set; }

        /// <summary>切り替えた結果を受け取るデリゲート。</summary>
        public Action<bool> CommitValue { get; set; }

        public float PreferredHeight { get; set; } = 18f;

        /// <summary>希望幅。丸が収まるだけあればよい。</summary>
        public float PreferredWidth { get; set; } = 18f;

        /// <summary>外周の丸の直径。</summary>
        public float CircleDiameter { get; set; } = 12f;

        /// <summary>オン時に描く内側の丸の、外周に対する比率。</summary>
        public float DotRatio { get; set; } = 0.6f;

        public bool Value => ValueProvider != null && ValueProvider();

        public override float Height() => PreferredHeight;

        public override float MinWidth() => PreferredWidth;

        /// <summary>外周の丸。矩形の中央に置く。</summary>
        private RectangleF CircleBounds
        {
            get
            {
                float d = Math.Min(CircleDiameter, Math.Min(Bounds.Width, Bounds.Height));
                return new RectangleF(
                    Bounds.X + (Bounds.Width - d) / 2f,
                    Bounds.Y + (Bounds.Height - d) / 2f,
                    d, d);
            }
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel != GH_CanvasChannel.Objects) return;
            if (Bounds.Width < 1f || Bounds.Height < 1f) return;

            RectangleF circle = CircleBounds;
            if (circle.Width < 1f) return;

            SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush fill = new SolidBrush(Enable ? Color.White : Color.FromArgb(226, 226, 226)))
                graphics.FillEllipse(fill, circle);

            using (Pen pen = new Pen(Enable ? Color.FromArgb(90, 90, 90) : Color.FromArgb(160, 160, 160), 1f))
                graphics.DrawEllipse(pen, circle);

            if (Value)
            {
                float inset = circle.Width * (1f - DotRatio) / 2f;
                RectangleF dot = RectangleF.Inflate(circle, -inset, -inset);

                using (SolidBrush brush = new SolidBrush(Enable ? Color.FromArgb(40, 40, 40) : Color.FromArgb(140, 140, 140)))
                    graphics.FillEllipse(brush, dot);
            }

            graphics.SmoothingMode = previous;
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!Enable || e.Button != MouseButtons.Left) return UIResponse.Ignore;

            // 丸だけだと小さすぎるので、割り当てられた矩形全体を当たり判定にする
            if (!Bounds.Contains(e.CanvasLocation)) return UIResponse.Ignore;

            CommitValue?.Invoke(!Value);
            return new UIResponse(GH_ObjectResponse.Handled, true);
        }
    } // class InlineToggleUI
}
