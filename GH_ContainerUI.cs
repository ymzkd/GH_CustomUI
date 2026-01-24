using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GH_CustomUI
{
    interface IContainerUI
    {
        List<GH_UIParts> UIParts { get; }

        System.Windows.Forms.Orientation Orientation { get; }

        RectangleF ContentBounds { get; }

        void AddUI(GH_UIParts ui);
    }

    public class ContainerUI : GH_UIParts, IContainerUI
    {
        public override bool TooltipEnabled => true;

        public List<GH_UIParts> UIParts { get; set; } = new List<GH_UIParts>();

        public RectangleF ContentBounds => this.Bounds;

        public System.Windows.Forms.Orientation Orientation { get; set; } = System.Windows.Forms.Orientation.Vertical;

        public ContainerUI() { }

        public override float Height()
        {
            if (Orientation == System.Windows.Forms.Orientation.Horizontal)
                return UIParts.Max(ui => ui.Height());
            else // Vertical
                return UIParts.Sum(ui => ui.Height());
        }

        public override float MinWidth()
        {
            if (Orientation == System.Windows.Forms.Orientation.Horizontal)
                return UIParts.Sum(ui => ui.MinWidth());
            else // Vertical
                return UIParts.Max(ui => ui.MinWidth());
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            foreach (var ui in UIParts)
                ui.Render(canvas, graphics, channel);
        }

        public override void UpdateLayout()
        {
            var base_pos = ContentBounds.Location;
            var base_width = ContentBounds.Width;
            foreach (var ui in UIParts)
            {
                if (Orientation == System.Windows.Forms.Orientation.Horizontal)
                {
                    ui.Bounds = new RectangleF(base_pos, new SizeF(ui.MinWidth(), ui.Height()));
                    base_pos.X += ui.MinWidth();
                }
                else // Vertical
                {
                    ui.Bounds = new RectangleF(base_pos, new SizeF(base_width, ui.Height()));
                    base_pos.Y += ui.Height();
                }
                ui.UpdateLayout();
            }
        }

        public void AddUI(GH_UIParts ui)
        {
            // Ownerがnullの場合は、例外をthrow
            if (Owner == null)
                throw new InvalidOperationException("ComponentUI must have an owner.");

            ui.Owner = Owner;
            UIParts.Add(ui);
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
            foreach (GH_UIParts ui in UIParts)
                ui.Write(writer);

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            foreach (GH_UIParts ui in UIParts)
                ui.Read(reader);

            return base.Read(reader);
        }
    }
}
