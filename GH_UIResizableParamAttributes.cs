using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GH_CustomUI
{
    public class GH_UIResizableParamAttributes<T> : GH_ResizableAttributes<T>, IPartsOwner where T : IGH_Param
    {
        /// <summary>
        /// ドラッグ操作中のUIパーツ。Capture を返したパーツをここに入れておくと、
        /// 以降の MouseMove / MouseUp が componentUIs を経由せず直接送られる。
        /// componentUIs に含めず手動配置しているパーツ(可変高のグラフなど)を
        /// ドラッグさせたい場合は、派生クラスから設定する。
        /// </summary>
        protected GH_UIParts ActiveObject;

        public GH_UIResizableParamAttributes(T owner) : base(owner)
        {
            RectangleF rect = this.Bounds;
            rect.Width = 200;
            rect.Height = 200;
            this.Bounds = rect;
        }

        public List<GH_UIParts> componentUIs { get; set; } = new List<GH_UIParts>();

        IGH_DocumentObject IPartsOwner.Owner => this.Owner; // 明示的に基底型で実装

        protected override Size MinimumSize => new Size(200, 200);

        protected override Padding SizingBorders => new Padding(5);

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
            Pivot = GH_Convert.ToPoint(Pivot);
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
            if (channel == GH_CanvasChannel.Wires)
            {
                this.RenderIncomingWires(canvas.Painter, Owner.Sources, Owner.WireDisplay);
            }
            else if (channel == GH_CanvasChannel.Objects)
            {
                GH_CapsuleRenderEngine.RenderInputGrip(graphics, canvas.Viewport.Zoom, InputGrip, true);
                GH_CapsuleRenderEngine.RenderOutputGrip(graphics, canvas.Viewport.Zoom, OutputGrip, true);

                GH_Capsule gH_Capsule = GH_Capsule.CreateCapsule(Bounds, GH_Palette.Blue);

                gH_Capsule.Render(graphics, Selected, locked: Owner.Locked, hidden: false);
                gH_Capsule.Dispose();
            }

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
                    Bounds.Height);

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
            }
            else // Componentの幅はUI描画に十分ある場合
            {
                // UI側の幅を拡張(UI幅をBounds.Widthとする)
                float yi = Bounds.Bottom - sumUIHeight;
                for (int i = 0; i < componentUIs.Count; i++)
                {
                    componentUIs[i].Bounds = new RectangleF(
                        Bounds.X, yi,
                        Bounds.Width, componentUIs[i].Height()
                        );

                    yi += componentUIs[i].Height();
                    componentUIs[i].UpdateLayout();
                }
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // Resizable Paramの実装を先行実行
            GH_ObjectResponse obj_response = base.RespondToMouseDown(sender, e);
            if (obj_response != GH_ObjectResponse.Ignore)
                return obj_response;

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
            // Resizable Paramの実装を先行実行
            GH_ObjectResponse obj_response = base.RespondToMouseUp(sender, e);
            if (obj_response != GH_ObjectResponse.Ignore)
                return obj_response;

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
            // Resizable Paramの実装を先行実行
            GH_ObjectResponse obj_response = base.RespondToMouseMove(sender, e);
            if (obj_response != GH_ObjectResponse.Ignore)
                return obj_response;

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
            {
                ui.Write(writer);
            }
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
