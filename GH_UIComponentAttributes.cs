using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using GH_IO.Serialization;

namespace GH_CustomUI
{
    public class GH_UIComponentAttributes : GH_ComponentAttributes, IPartsOwner
    {
        GH_UIParts ActiveObject;

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
            foreach (GH_UIParts ui in componentUIs)
                ui.Write(writer);

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            foreach (GH_UIParts ui in componentUIs)
                ui.Read(reader);

            return base.Read(reader);
        }
    } // GH_UIComponentAttributes




}
