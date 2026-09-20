using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using GH_IO.Serialization;

namespace GH_CustomUI
{
    public class GH_UIComponentAttributes : GH_ComponentAttributes, IPartsOwner
    {
        /// <summary>
        /// ドラッグ操作中のUIパーツ。Capture を返したパーツをここに入れておくと、
        /// 以降の MouseMove / MouseUp が componentUIs を経由せず直接送られる。
        /// </summary>
        protected GH_UIParts ActiveObject;

        public List<GH_UIParts> componentUIs { get; set; } = new List<GH_UIParts>();

        public GH_UIComponentAttributes(IGH_Component owner)
            : base(owner)
        {
        }

        IGH_DocumentObject IPartsOwner.Owner => this.Owner; // 明示的に基底型で実装

        /// <summary>
        /// Inline UI列とコンポーネント本体の間に取る余白。
        /// </summary>
        public float InlineUIPadding { get; set; } = 4f;

        /// <summary>
        /// Inline UIを持つInput Param。<see cref="Layout"/>で作り直す。
        /// </summary>
        /// <remarks>
        /// Renderはチャンネルごとに毎フレーム呼ばれるため、そのたびに
        /// Params.Inputを走査して絞り込むとフレームあたりの負荷になる。
        /// 配列で持つのはforeachでイテレータを確保させないため。
        /// </remarks>
        protected IInlineParamUI[] InlineParamUIs { get; private set; } = new IInlineParamUI[0];

        /// <summary>
        /// Inline UIを持つParamを拾い直す。可変パラメータの増減や
        /// ファイル読み込みに追随するため、Layoutのたびに作り直す。
        /// </summary>
        private void CollectInlineParams()
        {
            List<IInlineParamUI> found = null;

            foreach (IGH_Param item in Owner.Params.Input)
            {
                if (!(item is IInlineParamUI param) || param.InlineUI == null) continue;

                if (found == null) found = new List<IInlineParamUI>();
                found.Add(param);
            }

            InlineParamUIs = found == null ? new IInlineParamUI[0] : found.ToArray();
        }

        /// <summary>
        /// イベントの配送先。Component UIパーツと、操作可能なInline Param UI。
        /// </summary>
        protected IEnumerable<GH_UIParts> DispatchTargets
        {
            get
            {
                foreach (GH_UIParts ui in componentUIs)
                    yield return ui;

                foreach (IInlineParamUI param in InlineParamUIs)
                    if (param.InlineUIEnabled)
                        yield return param.InlineUI;
            }
        }

        /// <summary>
        /// UIパーツとこのAttributeを相互参照しつつ登録する。
        /// </summary>
        /// <param name="ui"></param>
        public void AddUI(GH_UIParts ui)
        {
            ui.Owner = this;

            // Serialize時の名前に使う登録順。明示指定されていれば尊重する
            if (ui.Index < 0) ui.Index = componentUIs.Count;

            componentUIs.Add(ui);
        }

        protected override void Layout()
        {
            // 基本幅・高さの計算
            base.Layout();
            CollectInlineParams();
            UpdateLayout();
        }

        public override void ExpireLayout()
        {
            base.ExpireLayout();
        }

        // Raises the DisplayExpired event on the toplevel object.
        public void OnDisplayExpired()
        {
            this.ExpireLayout();
            Owner.OnDisplayExpired(true);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            // Componentカプセル描画前後でカプセルを入れ替え
            base.Render(canvas, graphics, channel);

            // UI Partsを逐次描画
            foreach (GH_UIParts ui in componentUIs)
                ui.Render(canvas, graphics, channel);

            // Param側が持つInline UIを逐次描画
            foreach (IInlineParamUI param in InlineParamUIs)
                param.InlineUI.Render(canvas, graphics, channel);
        }

        /// <summary>
        /// 各UIが申告した希望サイズを集約してコンポーネント本体を広げ、
        /// 標準のLayout helperで再配置したうえで各UIへ最終的な矩形を返す。
        /// </summary>
        /// <remarks>
        /// 広げる対象はAttributes全体の<see cref="GH_Attributes{T}.Bounds"/>ではなく
        /// Component本体である<c>m_innerBounds</c>。Input/Outputや
        /// StateTagを含む最終的なBoundsはLayoutBoundsに任せる。
        /// </remarks>
        protected void UpdateLayout()
        {
            // ---- 1. 各UIが要求するサイズを集計 ----
            float partsWidth = componentUIs.Count == 0 ? 0f : componentUIs.Max(x => x.MinWidth());
            float partsHeight = componentUIs.Count == 0 ? 0f : componentUIs.Sum(x => x.Height());

            float inlineWidth = 0f;
            foreach (IInlineParamUI param in InlineParamUIs)
                inlineWidth = Math.Max(inlineWidth, param.PreferredUiSize.Width);

            // ---- 2. Component本体(m_innerBounds)の必要幅を決める ----
            RectangleF box = m_innerBounds;
            float nameBoxWidth = box.Width;

            float requiredWidth = nameBoxWidth;

            // Inline UI列はComponent本体の中に確保する
            if (inlineWidth > 0f)
                requiredWidth = nameBoxWidth + inlineWidth + InlineUIPadding * 2f;

            // Component UIパーツはAttributes全体の幅を要求するので、その差分を本体に足す
            if (partsWidth > Bounds.Width)
                requiredWidth = Math.Max(requiredWidth, nameBoxWidth + (partsWidth - Bounds.Width));

            // ---- 3. m_innerBoundsを広げて標準Layout helperで再配置 ----
            if (requiredWidth > box.Width)
            {
                float dw = requiredWidth - box.Width;
                box = new RectangleF(box.X - dw / 2f, box.Y, requiredWidth, box.Height);
                m_innerBounds = box;

                LayoutInputParams(Owner, box);
                LayoutOutputParams(Owner, box);
                Bounds = LayoutBounds(Owner, box);
            }

            // ---- 4. Inline UIの最終矩形を確定してParamへ通知し、アイコン枠を退避 ----
            LayoutInlineParameterUIs(box, nameBoxWidth, inlineWidth);

            // ---- 5. Component UIパーツはBoundsの下端に積む ----
            // partsHeightは全パーツの合計なので、ここを飛ばすのは
            // 積むべき高さを持つパーツが1つも無いときだけ。
            if (partsHeight > 0f)
            {
                Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + partsHeight);

                float yi = Bounds.Bottom - partsHeight;
                foreach (GH_UIParts ui in componentUIs)
                {
                    ui.Bounds = new RectangleF(Bounds.X, yi, Bounds.Width, ui.Height());
                    yi += ui.Height();
                    ui.UpdateLayout();
                }
            }
        }

        /// <summary>
        /// 再Layout後のparam.Attributes.Boundsを基準に、各Inline UIの矩形を決める。
        /// </summary>
        private void LayoutInlineParameterUIs(RectangleF box, float nameBoxWidth, float inlineWidth)
        {
            if (inlineWidth <= 0f) return;

            // Inline UIを並べる列。このメソッドの中だけで完結する。
            RectangleF column = new RectangleF(
                box.X + InlineUIPadding, box.Y, inlineWidth, box.Height);

            // 広げたままだとGrasshopperがアイコン/名前を箱の中央、つまりUI列の上に描く。
            // Input/Outputの配置が終わったここでアイコン枠ぶんまで縮めておくと、
            // 標準描画のままUI列の右隣へ収まる。
            // 縮めるのはレイアウト計算の後なので、Params や Bounds には影響しない。
            m_innerBounds = new RectangleF(
                box.Right - nameBoxWidth, box.Y, nameBoxWidth, box.Height);

            foreach (IInlineParamUI param in InlineParamUIs)
            {
                param.InlineUI.Owner = this;

                RectangleF row = ((IGH_Param)param).Attributes.Bounds;
                SizeF preferred = param.PreferredUiSize;

                // 列幅は各Paramのpreferred.Widthの最大値なので、幅は申告どおりに使う。
                // 高さは行に合わせて詰める必要があるのでこちらは上限を掛ける。
                float h = Math.Min(preferred.Height, row.Height);
                float w = preferred.Width;

                RectangleF uiBounds = new RectangleF(
                    column.X,
                    row.Y + (row.Height - h) / 2f,
                    w, h);

                param.LayoutInlineUI(GH_Convert.ToRectangle(uiBounds));
            }
        }

        /// <summary>
        /// UIパーツへイベントを配り、Captureを返したパーツを掴み続ける。
        /// </summary>
        /// <param name="handler">各パーツへ渡すイベント</param>
        /// <param name="fallback">
        /// どのパーツも拾わなかったときの転送先。
        /// 掴んでいるパーツがある間は呼ばない。
        /// </param>
        private GH_ObjectResponse DispatchToUIParts(Func<GH_UIParts, UIResponse> handler,
            Func<GH_ObjectResponse> fallback = null)
            => GH_UIDispatch.ToParts(DispatchTargets, ref ActiveObject,
                                     handler, OnDisplayExpired, fallback);

        /// <remarks>
        /// UIパーツが誰も拾わなかったイベントは基底へ転送する。
        /// 可変パラメータのZUI(+/-)のクリックは<see cref="GH_ComponentAttributes"/>が
        /// <c>RespondToMouseDown</c>だけで処理しているので、転送しないと
        /// ZUIを押しても何も起きない。
        /// 残る5つはGrasshopper 8.35時点では基底が何もしない(IL 2バイト、
        /// Ignoreを返すだけ)ため転送しても影響は無いが、将来実装された場合に
        /// 追随できるよう同じ形で揃えてある。
        /// 転送するのは掴んでいるパーツが無いときだけなので、
        /// Dropdown展開中に基底へ流れることはない。
        /// </remarks>
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
            => DispatchToUIParts(ui => ui.RespondToMouseDown(sender, e),
                                 () => base.RespondToMouseDown(sender, e));

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
            => DispatchToUIParts(ui => ui.RespondToMouseUp(sender, e),
                                 () => base.RespondToMouseUp(sender, e));

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
            => DispatchToUIParts(ui => ui.RespondToMouseMove(sender, e),
                                 () => base.RespondToMouseMove(sender, e));

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
            => DispatchToUIParts(ui => ui.RespondToMouseDoubleClick(sender, e),
                                 () => base.RespondToMouseDoubleClick(sender, e));

        public override GH_ObjectResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
            => DispatchToUIParts(ui => ui.RespondToKeyDown(sender, e),
                                 () => base.RespondToKeyDown(sender, e));

        public override GH_ObjectResponse RespondToKeyUp(GH_Canvas sender, KeyEventArgs e)
            => DispatchToUIParts(ui => ui.RespondToKeyUp(sender, e),
                                 () => base.RespondToKeyUp(sender, e));

        public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            base.SetupTooltip(canvasPoint, e);

            foreach (GH_UIParts ui in componentUIs)
                if (ui is IGH_TooltipAwareObject tooltipAwareObject)
                    tooltipAwareObject.SetupTooltip(canvasPoint, e);

            foreach (IInlineParamUI param in InlineParamUIs)
                if (param.InlineUI is IGH_TooltipAwareObject tooltipAwareObject)
                    tooltipAwareObject.SetupTooltip(canvasPoint, e);
        }

        public override bool Write(GH_IWriter writer)
        {
            // 一つのUIパーツの失敗で他のパーツの保存が止まらないようにする
            foreach (GH_UIParts ui in componentUIs)
            {
                try { ui.Write(writer); }
                catch (Exception) { }
            }

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            // 保存形式が変わったUIパーツがあっても、他のパーツの読み込みは継続する。
            // 読めなかったパーツは既定値のままとなる。
            foreach (GH_UIParts ui in componentUIs)
            {
                try { ui.Read(reader); }
                catch (Exception) { }
            }

            return base.Read(reader);
        }
    } // GH_UIComponentAttributes




}
