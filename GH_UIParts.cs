using GH_IO.Serialization;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

namespace GH_CustomUI
{
    public interface IPartsOwner : IGH_Attributes
    {
        List<GH_UIParts> componentUIs { get; set; }
        IGH_DocumentObject Owner { get; }

        void OnDisplayExpired();
    }

    public struct TooltipData
    {
        /// <summary>
        /// Gets or sets the description of the tooltip. This field is optional.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the image that is displayed in the details section of the tooltip. This field is optional, \
        /// but when it is defined, the Description property is ignored.
        /// </summary>
        public Bitmap Diagram { get; set; }

        /// <summary>
        /// Gets or sets the icon that is displayed in the upper left hand corner of the tooltip. This field is optional.
        /// </summary>
        public Bitmap Icon { get; set; }

        /// <summary>
        /// Gets or sets the text of the tooltip. If you do not set the Text property,
        /// you must set the Title property or the tooltip will not be shown.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets the title of the tooltip. If you do not set the Title property,
        /// you must set the Text property or the tooltip will not be shown.
        /// </summary>
        public string Title { get; set; }

        public TooltipData() { }
    }

    /// <summary>
    /// 何らかの操作の結果、ExpireとコンポーネントのObjectをどうするかを指定
    /// </summary>
    public struct UIResponse
    {
        public GH_ObjectResponse Response;

        /// <summary>
        /// UIを再描画するかどうか。
        /// </summary>
        public bool Redraw { get; set; }

        public UIResponse(GH_ObjectResponse r, bool redraw = true)
        {
            Response = r;
            Redraw = redraw;
        }

        public static UIResponse Ignore
            => new UIResponse(GH_ObjectResponse.Ignore, false);

        public static UIResponse Default
            => new UIResponse(GH_ObjectResponse.Handled);

        public static UIResponse Handled => Default;
    }

    // あまり活用されていない。
    [Flags]
    public enum ButtonRoundSide
    {
        Left = 1,
        Right = 2,
        Both = Left | Right
    }

    /// <summary>
    /// ラベルとコントロール(チェックボックス/ラジオボタン)の並び
    /// </summary>
    public enum LabelOrientation
    {
        /// <summary>ラベルが上、コントロールが下</summary>
        Vertical,
        /// <summary>ラベルが左、コントロールが右</summary>
        Horizontal
    }

    /// <summary>
    /// ComponentUIパーツの基本クラス
    /// </summary>
    public abstract class GH_UIParts : IGH_TooltipAwareObject
    {
        /// <summary>
        /// 内部に複数のUIパーツを有するパーツ向けのプロパティ
        /// パーツサブセットにおいてActiveなパーツを格納する。
        /// </summary>
        public GH_UIParts ActiveObject { get; set; } = null;

        public virtual IPartsOwner Owner { get; set; } = null;

        /// <summary>
        /// UIが描画される領域、OwnerのUpdateLayoutメソッドにより更新される。
        /// </summary>
        public RectangleF Bounds { get; set; }

        /// <summary>
        /// UIの高さを計算するメソッド
        /// </summary>
        public abstract float Height();

        public bool Enable { get; set; } = true;

        /// <summary>
        /// UIの描画に必要な最小幅を計算するメソッド
        /// </summary>
        /// <returns>最小幅</returns>
        public abstract float MinWidth();

        public abstract void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel);

        public virtual void UpdateLayout() { }

        /// <summary>
        /// Serialize時にはユニークな名前をつける必要があるので、
        /// UIパーツの登録順をクラス名に足して名前とする。
        /// </summary>
        public string UniqueName
        {
            get
            {
                int idx = Owner == null ? 0 : Owner.componentUIs.IndexOf(this);
                return $"{this.GetType().Name}{idx}";
            }
        }

        public TooltipData? TooltipData { get; set; } = null;

        public virtual bool TooltipEnabled => TooltipData != null;

        /// <summary>
        /// 仮にこれだけ実装。他のUIイベントも実装する。
        /// </summary>
        public virtual UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
            => UIResponse.Ignore;

        public virtual UIResponse RespondToMouseUp(GH_Canvas sender, GH_CanvasMouseEvent e)
            => UIResponse.Ignore;

        public virtual UIResponse RespondToMouseDoubleClick(GH_Canvas sender, GH_CanvasMouseEvent e)
            => UIResponse.Ignore;

        public virtual UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
            => UIResponse.Ignore;

        public virtual UIResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
            => UIResponse.Ignore;

        public virtual UIResponse RespondToKeyUp(GH_Canvas sender, KeyEventArgs e)
            => UIResponse.Ignore;

        public virtual bool Write(GH_IWriter writer) => true;

        public virtual bool Read(GH_IReader reader) => true;

        public static float MaxTextWidth(IEnumerable<string> spacerTxts, Font font)
        {
            float sp = new float(); //width of spacer text

            // adjust fontsize to high resolution displays
            foreach (string s in spacerTxts)
            {
                if (GH_FontServer.StringWidth(s, font) > sp)
                    sp = GH_FontServer.StringWidth(s, font);
            }

            return sp;
        }

        public static float MaxTextWidth<T>(IEnumerable<T> spacerTxts, Font font)
        {
            float sp = new float(); //width of spacer text

            // adjust fontsize to high resolution displays
            foreach (T s in spacerTxts)
            {
                if (GH_FontServer.StringWidth(s.ToString(), font) > sp)
                    sp = GH_FontServer.StringWidth(s.ToString(), font);
            }

            return sp;
        }

        /// <summary>
        ///  overlay -> ボタンの上のほうのゾーンの少し明るい部分のことみたい
        /// </summary>
        public static GraphicsPath RoundedRect(RectangleF bounds, int radius,
            ButtonRoundSide roundSide = ButtonRoundSide.Both, bool overlay = false)
        {
            RectangleF b = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            int diameter = radius * 2;
            Size size = new Size(diameter, diameter);
            RectangleF arc = new RectangleF(b.Location, size);
            GraphicsPath path = new GraphicsPath();

            if (overlay)
                b.Height = diameter;

            if (radius == 0)
            {
                path.AddRectangle(b);
                return path;
            }

            // top left arc
            if (roundSide.HasFlag(ButtonRoundSide.Left))
                path.AddArc(arc, 180, 90);
            else
                path.AddLine(arc.X, arc.Y - radius, arc.X, arc.Y);

            // top right arc
            arc.X = b.Right - diameter;
            if (roundSide.HasFlag(ButtonRoundSide.Right))
                path.AddArc(arc, 270, 90);
            else
                path.AddLine(arc.Right, arc.Top, arc.Right, arc.Bottom);

            if (!overlay)
            {
                // bottom right arc
                arc.Y = b.Bottom - diameter;
                if (roundSide.HasFlag(ButtonRoundSide.Right))
                    path.AddArc(arc, 0, 90);
                else
                    path.AddLine(arc.Right, arc.Top, arc.Right, arc.Bottom);


                // bottom left arc
                arc.X = b.Left;
                if (roundSide.HasFlag(ButtonRoundSide.Left))
                    path.AddArc(arc, 90, 90);
                else
                    path.AddLine(arc.Left, arc.Bottom, arc.Left, arc.Right);

            }
            else
            {
                path.AddLine(new PointF(b.X + b.Width, b.Y + b.Height), new PointF(b.X, b.Y + b.Height));
            }

            path.CloseFigure();
            return path;
        }

        public static GraphicsPath CapsulePath(RectangleF bounds)
            => RoundedRect(bounds, (int)(bounds.Height * 0.5f));

        public static void DrawDropDownButton(Graphics graphics, PointF center,
            Color colour, int rectanglesize, bool upsideDown = false)
        {
            Pen pen = new Pen(new SolidBrush(colour))
            {
                Width = rectanglesize / 8
            };

            if (upsideDown)
            {
                graphics.DrawLines(
                    pen, new PointF[]
                    {
                        new PointF(center.X - rectanglesize / 4, center.Y + rectanglesize / 8),
                        new PointF(center.X, center.Y - rectanglesize / 6),
                        new PointF(center.X + rectanglesize / 4, center.Y + rectanglesize / 8)
                    });
            }
            else
            {
                graphics.DrawLines(
                    pen, new PointF[]
                    {
                        new PointF(center.X - rectanglesize / 4, center.Y - rectanglesize / 8),
                        new PointF(center.X, center.Y + rectanglesize / 6),
                        new PointF(center.X + rectanglesize / 4, center.Y - rectanglesize / 8)
                    });
            }
        }

        public virtual bool IsTooltipRegion(PointF canvasPoint)
        {
            return Bounds.Contains(canvasPoint);
        }

        public virtual void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            if (!TooltipEnabled || !IsTooltipRegion(canvasPoint) || TooltipData==null)
                return;

            if (TooltipData?.Description is not null) e.Description = TooltipData?.Description;
            if (TooltipData?.Diagram is not null) e.Diagram = TooltipData?.Diagram;
            if (TooltipData?.Icon is not null) e.Icon = TooltipData?.Icon;
            if (TooltipData?.Text is not null) e.Text = TooltipData?.Text;
            if (TooltipData?.Title is not null) e.Title = TooltipData?.Title;
        }
    } // GH_UIParts
}
