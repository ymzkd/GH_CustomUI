using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// Param 1行の中に収まる高さの、クリックで編集できる値ボックス。
    /// </summary>
    /// <remarks>
    /// 値そのものは保持せず、表示は<see cref="TextProvider"/>、
    /// 確定は<see cref="CommitText"/>へ委譲する。
    /// 値の本体をParamのPersistentData側だけに置き、二重管理を避けるため。
    /// </remarks>
    public class InlineTextFieldUI : GH_UIParts
    {
        private class InputTextField : Grasshopper.GUI.Base.GH_TextBoxInputBase
        {
            private readonly InlineTextFieldUI m_ui;

            public InputTextField(InlineTextFieldUI ui)
            {
                m_ui = ui;
            }

            protected override void HandleTextInputAccepted(string text)
                => m_ui.Commit(text);
        }

        private readonly Font m_font;

        public InlineTextFieldUI()
        {
            Font f = GH_FontServer.Standard;
            m_font = new Font(f.FontFamily, f.Size / GH_GraphicsUtil.UiScale, f.Style);
        }

        /// <summary>表示する文字列を返すデリゲート。</summary>
        public Func<string> TextProvider { get; set; }

        /// <summary>入力された文字列を受け取り、採用できたらtrueを返すデリゲート。</summary>
        public Func<string, bool> CommitText { get; set; }

        public string Placeholder { get; set; } = "";

        /// <summary>
        /// 編集ボックスの右端でOK/Cancelボタンが占める幅(画面px)。
        /// </summary>
        /// <remarks>
        /// Grasshopper側がズームによらず固定で確保する領域で、実測値は62px。
        /// 入力欄の実幅は<c>Bounds.Width × zoom + 2 - この値</c>となる。
        /// ボタンの占有幅が画面px固定である以上、入力欄が潰れるかどうかは
        /// そのコンポーネントをどの倍率で表示しているかで決まる。
        /// 例えば幅52pxの矩形なら等倍で0px、1.5倍なら18px使える。
        /// したがって<see cref="PreferredWidth"/>に「これ以上なら安全」という
        /// 静的な下限値は定義できない。
        /// Grasshopper側の描画が変わったときに追随できるようstaticにしてある。
        /// </remarks>
        public static float EditorButtonWidth { get; set; } = 62f;

        /// <summary>
        /// <see cref="PreferredWidth"/>の既定値を決めるときに、
        /// 入力欄として確保する幅(画面px、等倍基準)。
        /// </summary>
        public static float DefaultTextWidth { get; set; } = 50f;

        /// <summary>
        /// 希望幅。表示値によって幅が動くとLayoutが頻繁に失効するため固定値で申告する。
        /// </summary>
        /// <remarks>
        /// クリックするとこの矩形のまま編集ボックスが開くので、
        /// 狭くしすぎると文字を打つ場所が無くなる。
        /// ただし必要な幅は表示倍率に依存する(<see cref="EditorButtonWidth"/>参照)ため、
        /// 引いたまま入力されがちか拡大して入力されるかを見て利用側が決める。
        /// 既定値は生成時に<see cref="EditorButtonWidth"/>と
        /// <see cref="DefaultTextWidth"/>から決まるため、
        /// それらの変更が効くのは以降に生成したウィジェットだけ。
        /// </remarks>
        public float PreferredWidth { get; set; } = EditorButtonWidth + DefaultTextWidth;

        public float PreferredHeight { get; set; } = 18f;

        public StringAlignment TextAlignment { get; set; } = StringAlignment.Center;

        public string DisplayText => TextProvider == null ? "" : TextProvider();

        public override float Height() => PreferredHeight;

        public override float MinWidth() => PreferredWidth;

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel != GH_CanvasChannel.Objects) return;
            if (Bounds.Width < 1f || Bounds.Height < 1f) return;

            RectangleF b = Bounds;

            using (SolidBrush fill = new SolidBrush(Enable ? Color.White : Color.FromArgb(226, 226, 226)))
                graphics.FillRectangle(fill, b);

            using (Pen pen = new Pen(Enable ? Color.FromArgb(90, 90, 90) : Color.FromArgb(160, 160, 160), 1f))
                graphics.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);

            string txt = DisplayText;
            bool placeholder = string.IsNullOrEmpty(txt);

            Color textColour = !Enable ? Color.FromArgb(120, 120, 120)
                : placeholder ? Color.Gray : Color.Black;

            using (StringFormat format = new StringFormat
            {
                Alignment = TextAlignment,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            using (SolidBrush tb = new SolidBrush(textColour))
                graphics.DrawString(placeholder ? Placeholder : txt, m_font, tb,
                    RectangleF.Inflate(b, -3f, 0f), format);
        }

        private void Commit(string text)
        {
            if (CommitText == null) return;
            if (CommitText(text))
                Owner?.OnDisplayExpired();
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!Enable || e.Button != MouseButtons.Left) return UIResponse.Ignore;
            if (!Bounds.Contains(e.CanvasLocation)) return UIResponse.Ignore;

            var matrix = sender.Viewport.XFormMatrix(GH_Viewport.GH_DisplayMatrix.CanvasToControl);
            var field = new InputTextField(this)
            {
                Bounds = GH_Convert.ToRectangle(Bounds),
                Font = m_font
            };

            field.ShowTextInputBox(sender, DisplayText, true, true, matrix);
            return new UIResponse(GH_ObjectResponse.Handled);
        }
    } // class InlineTextFieldUI
}
