using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using System.Reflection;
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

        private static readonly FieldInfo s_renderTagsField =
            typeof(GH_LinkedParamAttributes).GetField(
                "m_renderTags",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public List<GH_UIParts> componentUIs { get; set; } = new List<GH_UIParts>();

        public GH_UIComponentAttributes(IGH_Component owner)
            : base(owner)
        {
        }

        IGH_DocumentObject IPartsOwner.Owner => this.Owner; // 明示的に基底型で実装

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
        }


        /// <summary>
        /// UIとコンポーネント幅を比較して幅の調整と
        /// マウスドラッグ等に伴う座標変更の適用
        ///
        /// 必要幅に応じた幅調整は初期で決定するはずなのに座標変更と
        /// 合わせて毎度調べなおしているのは不自然かも。
        /// </summary>
        protected void UpdateLayout()
        {
            // Component本体とUIのレイアウトを調整
            float maxUIWidth = componentUIs.Max(x => x.MinWidth());
            float sumUIHeight = componentUIs.Sum(x => x.Height());

            if (maxUIWidth > Bounds.Width) // Component幅がUIの必要幅より狭い場合
            {
                // コンポーネント側の幅を拡張(コンポーネント幅をmaxUIWidthとする)
                float spread_width = maxUIWidth - Bounds.Width;
                Bounds = new RectangleF(
                    Bounds.X - spread_width / 2f,
                    Bounds.Y,
                    maxUIWidth,
                    Bounds.Height + sumUIHeight);

                // UIのレイアウトを更新
                float yi = Bounds.Bottom - sumUIHeight;
                for (int i = 0; i < componentUIs.Count; i++)
                {
                    componentUIs[i].Bounds = new RectangleF(
                        Bounds.X, yi,
                        maxUIWidth, componentUIs[i].Height());
                    yi += componentUIs[i].Height();
                    componentUIs[i].UpdateLayout();
                }

                // 出力パラメータのテキストを更新
                foreach (IGH_Param item in Owner.Params.Output)
                {
                    // Output ParamのAnchorとTextBoxを取得
                    PointF pivot = item.Attributes.Pivot;
                    RectangleF bounds = item.Attributes.Bounds;
                    item.Attributes.Pivot = new PointF(
                        pivot.X + spread_width / 2f, // X座標だけ右に移動
                        pivot.Y);
                    item.Attributes.Bounds = new RectangleF(
                        bounds.X + spread_width / 2f, // X座標だけ右に移動
                        bounds.Y,
                        bounds.Width,
                        bounds.Height);
                }

                // 入力パラメータのテキストを更新
                float inputwidth = (Owner.Params.Input.Count > 0) ?
                    Owner.Params.Input.Max(item => item.Attributes.Bounds.Width) : 0f;
                foreach (IGH_Param item in Owner.Params.Input)
                {
                    // Input ParamのAnchorとTextBoxを取得
                    PointF pivot = item.Attributes.Pivot;
                    RectangleF bounds = item.Attributes.Bounds;
                    item.Attributes.Pivot = new PointF(
                        pivot.X - spread_width / 2f + inputwidth,
                        pivot.Y);
                    item.Attributes.Bounds = new RectangleF(
                        bounds.X - spread_width / 2f,
                        bounds.Y,
                        bounds.Width,
                        bounds.Height);
                }

                // StateTagアイコン位置を再配置
                RelayoutStateTags();
            }
            else // Componentの幅はUI描画に十分ある場合
            {
                // Component領域は高さをUIの分拡張する
                Bounds = new RectangleF(
                    Bounds.X,
                    Bounds.Y,
                    Bounds.Width,
                    Bounds.Height + sumUIHeight);

                // UI側の幅を拡張(UI幅をBounds.Widthとする)
                float yi = Bounds.Bottom - sumUIHeight;
                for (int i = 0; i < componentUIs.Count; i++)
                {
                    componentUIs[i].Bounds = new RectangleF(
                        Bounds.X, yi,
                        Bounds.Width, componentUIs[i].Height());
                    yi += componentUIs[i].Height();
                    componentUIs[i].UpdateLayout();
                }
            }
        }

        /// <summary>
        /// パラメータのStateTag(Flatten/Graft/Simplify)アイコン位置を再配置する。
        /// UI拡張によりパラメータ位置が変更された後に呼び出すこと。
        /// </summary>
        private void RelayoutStateTags()
        {
            if (s_renderTagsField == null)
                return;

            // 各側のStateTagアイコン最大幅を計算 (1アイコン = 20px)
            int maxInputIconWidth = 0;
            foreach (IGH_Param param in Owner.Params.Input)
            {
                int iconWidth = ((List<IGH_StateTag>)param.StateTags).Count * 20;
                maxInputIconWidth = System.Math.Max(maxInputIconWidth, iconWidth);
            }

            int maxOutputIconWidth = 0;
            foreach (IGH_Param param in Owner.Params.Output)
            {
                int iconWidth = ((List<IGH_StateTag>)param.StateTags).Count * 20;
                maxOutputIconWidth = System.Math.Max(maxOutputIconWidth, iconWidth);
            }

            // --- 入力パラメータ側 ---
            foreach (IGH_Param param in Owner.Params.Input)
            {
                GH_LinkedParamAttributes paramAttr = param.Attributes as GH_LinkedParamAttributes;
                if (paramAttr == null)
                    continue;

                GH_StateTagList tags = param.StateTags;
                if (((List<IGH_StateTag>)tags).Count == 0)
                {
                    s_renderTagsField.SetValue(paramAttr, null);
                    continue;
                }

                // ラベル領域のみの矩形にする（左側にアイコン分の余白を確保）
                Rectangle rect = GH_Convert.ToRectangle(paramAttr.Bounds);
                rect.X += maxInputIconWidth;
                rect.Width -= maxInputIconWidth;
                tags.Layout(rect, (GH_StateTagLayoutDirection)0);

                Rectangle tagBox = tags.BoundingBox;
                if (!tagBox.IsEmpty)
                    paramAttr.Bounds = RectangleF.Union(paramAttr.Bounds, tagBox);

                s_renderTagsField.SetValue(paramAttr, tags);
            }

            // --- 出力パラメータ側 ---
            foreach (IGH_Param param in Owner.Params.Output)
            {
                GH_LinkedParamAttributes paramAttr = param.Attributes as GH_LinkedParamAttributes;
                if (paramAttr == null)
                    continue;

                GH_StateTagList tags = param.StateTags;
                if (((List<IGH_StateTag>)tags).Count == 0)
                {
                    s_renderTagsField.SetValue(paramAttr, null);
                    continue;
                }

                // ラベル領域のみの矩形にする（右側にアイコン分の余白を確保）
                Rectangle rect = GH_Convert.ToRectangle(paramAttr.Bounds);
                rect.Width -= maxOutputIconWidth;
                tags.Layout(rect, GH_StateTagLayoutDirection.Right);

                Rectangle tagBox = tags.BoundingBox;
                if (!tagBox.IsEmpty)
                    paramAttr.Bounds = RectangleF.Union(paramAttr.Bounds, tagBox);

                s_renderTagsField.SetValue(paramAttr, tags);
            }

            // --- 入力側の左端を揃える ---
            if (Owner.Params.Input.Count > 0)
            {
                float minX = Owner.Params.Input.Min(p => p.Attributes.Bounds.X);
                foreach (IGH_Param p in Owner.Params.Input)
                {
                    RectangleF b = p.Attributes.Bounds;
                    p.Attributes.Bounds = new RectangleF(minX, b.Y, b.Right - minX, b.Height);
                }
            }

            // --- 出力側の右端を揃える ---
            if (Owner.Params.Output.Count > 0)
            {
                float maxRight = Owner.Params.Output.Max(p => p.Attributes.Bounds.Right);
                foreach (IGH_Param p in Owner.Params.Output)
                {
                    RectangleF b = p.Attributes.Bounds;
                    p.Attributes.Bounds = new RectangleF(b.X, b.Y, maxRight - b.X, b.Height);
                }
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
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
                foreach (GH_UIParts ui in componentUIs)
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

            if (response.Redraw)
                this.OnDisplayExpired();

            return response.Response;
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
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
                foreach (GH_UIParts ui in componentUIs)
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

            if (response.Redraw)
                this.OnDisplayExpired();

            return response.Response;
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
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
                foreach (GH_UIParts ui in componentUIs)
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

            if (response.Redraw)
                this.OnDisplayExpired();

            return response.Response;
        }

        public override GH_ObjectResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            UIResponse response = UIResponse.Ignore;
            if (ActiveObject != null)
            {
                response = ActiveObject.RespondToMouseDoubleClick(sender, e);

                if (response.Response == GH_ObjectResponse.Release)
                    ActiveObject = null;
            }
            else
            {
                foreach (GH_UIParts ui in componentUIs)
                {
                    response = ui.RespondToMouseDoubleClick(sender, e);
                    if (response.Response == GH_ObjectResponse.Ignore)
                        continue;
                    else if (response.Response == GH_ObjectResponse.Capture)
                        ActiveObject = ui;

                    // 操作終了(Ignore以外)
                    break;
                }
            }

            if (response.Redraw)
                this.OnDisplayExpired();

            return response.Response;
        }

        public override GH_ObjectResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
        {
            UIResponse response = UIResponse.Ignore;
            if (ActiveObject != null)
            {
                response = ActiveObject.RespondToKeyDown(sender, e);

                if (response.Response == GH_ObjectResponse.Release)
                    ActiveObject = null;
            }
            else
            {
                foreach (GH_UIParts ui in componentUIs)
                {
                    response = ui.RespondToKeyDown(sender, e);
                    if (response.Response == GH_ObjectResponse.Ignore)
                        continue;
                    else if (response.Response == GH_ObjectResponse.Capture)
                        ActiveObject = ui;

                    // 操作終了(Ignore以外)
                    break;
                }
            }

            if (response.Redraw)
                this.OnDisplayExpired();

            return response.Response;
        }

        public override GH_ObjectResponse RespondToKeyUp(GH_Canvas sender, KeyEventArgs e)
        {
            UIResponse response = UIResponse.Ignore;
            if (ActiveObject != null)
            {
                response = ActiveObject.RespondToKeyUp(sender, e);

                if (response.Response == GH_ObjectResponse.Release)
                    ActiveObject = null;
            }
            else
            {
                foreach (GH_UIParts ui in componentUIs)
                {
                    response = ui.RespondToKeyUp(sender, e);
                    if (response.Response == GH_ObjectResponse.Ignore)
                        continue;
                    else if (response.Response == GH_ObjectResponse.Capture)
                        ActiveObject = ui;

                    // 操作終了(Ignore以外)
                    break;
                }
            }

            if (response.Redraw)
                this.OnDisplayExpired();

            return response.Response;
        }

        public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            base.SetupTooltip(canvasPoint, e);

            foreach (GH_UIParts ui in componentUIs)
                if (ui is IGH_TooltipAwareObject tooltipAwareObject)
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
