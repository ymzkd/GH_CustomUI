using GH_IO.Serialization;
using System;
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
            if (componentUIs.Count == 0) return;

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

        /// <summary>
        /// UIパーツへイベントを配る。掴んでいるパーツの管理は共通実装に任せる。
        /// </summary>
        private GH_ObjectResponse DispatchToUIParts(Func<GH_UIParts, UIResponse> handler,
            Func<GH_ObjectResponse> fallback = null)
            => GH_UIDispatch.ToParts(componentUIs, ref ActiveObject,
                                     handler, OnDisplayExpired, fallback);

        /// <remarks>
        /// リサイズ枠の操作は<see cref="GH_ResizableAttributes{T}"/>が持っているので、
        /// Mouse系は基底を先に通してからUIパーツへ配る。
        /// Component側のホストが基底を「誰も拾わなかったときの転送先」に
        /// しているのと順序が逆なのはこのため。
        /// </remarks>
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            GH_ObjectResponse resize = base.RespondToMouseDown(sender, e);
            if (resize != GH_ObjectResponse.Ignore) return resize;

            return DispatchToUIParts(ui => ui.RespondToMouseDown(sender, e));
        }

        public override GH_ObjectResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            GH_ObjectResponse resize = base.RespondToMouseUp(sender, e);
            if (resize != GH_ObjectResponse.Ignore) return resize;

            return DispatchToUIParts(ui => ui.RespondToMouseUp(sender, e));
        }

        public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            GH_ObjectResponse resize = base.RespondToMouseMove(sender, e);
            if (resize != GH_ObjectResponse.Ignore) return resize;

            return DispatchToUIParts(ui => ui.RespondToMouseMove(sender, e));
        }

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
