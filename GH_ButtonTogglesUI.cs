using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GH_CustomUI
{
    public enum ButtonGroupSelectionType
    {
        RadioGroup, Multiple
    }

    public enum ButtonGroupSpacingType
    {
        Compact, Uniform
    }

    /// <summary>
    /// 複数のボタンが並んだUIで、
    /// 一つを選択するRadioButton形式と複数選択できるCheckBoxes形式を
    /// プロパティで切り替えることが出来る。
    /// </summary>
    public class ButtonTogglesUI : GH_UIParts
    {
        private bool mouseOver = false;

        public Action CheckedChanged { get; set; } = () => { };

        public Brush BackGroudFillBrush { get; set; } = Brushes.LightGray;

        #region Layout Parameters

        private RectangleF ContentBounds
            => new RectangleF(new PointF(Bounds.Location.X + Margin, Bounds.Location.Y),
                new SizeF(Bounds.Width - Margin * 2, Bounds.Height - Margin));

        public float Margin = 2f;
        private float RoundedCornerRadius => Margin;
        #endregion

        public string Label;
        private Font labelFont = GH_FontServer.Standard;

        public List<ToggleButtonUI> Buttons { get; set; } = new List<ToggleButtonUI>();

        public ButtonGroupSelectionType SelectionType { get; set; } = ButtonGroupSelectionType.RadioGroup;

        public ButtonGroupSpacingType SpacingType { get; set; } = ButtonGroupSpacingType.Compact;

        private IPartsOwner m_owner = null;

        public override IPartsOwner Owner
        {
            get => m_owner;
            set
            {
                m_owner = value;
                foreach (var p in Buttons)
                    p.Owner = value;
            }
        }

        /// <summary>
        /// 要素の中から選択要素のインデックスを取得。
        /// マルチセレクト形式の時にすべての選択インデックスを取得するためのプロパティ
        /// どの要素も選択されていなかったら空の配列が取得される。
        /// </summary>
        public int[] SelectedIndices
            => selected_indices().ToArray();

        private IEnumerable<int> selected_indices()
        {
            for (int i = 0; i < Buttons.Count; i++)
                if (Buttons[i].Checked)
                    yield return i;
        }

        /// <summary>
        /// 要素の中から最初の選択要素インデックスを取得。
        /// ラジオボタン形式の時に唯一の選択インデックスを取得するためのプロパティ
        /// どの要素も選択されていなかったら-1が取得される。
        /// </summary>
        public int SelectedIndex
        {
            get
            {
                for (int i = 0; i < Buttons.Count; i++)
                    if (Buttons[i].Checked)
                        return i;
                return -1;
            }
        }

        private bool[] Toggles => Buttons.Select(x => x.Checked).ToArray();


        public void SelectItem(int index)
        {
            if (SelectionType == ButtonGroupSelectionType.RadioGroup)
            {
                for (int i = 0; i < Buttons.Count; i++)
                    Buttons[i].Checked = (index == i);
            }
            else
                Buttons[index].Checked = !Buttons[index].Checked;

            OnCheckedChanged();
        }

        public ButtonTogglesUI()
        {
            Bounds = new RectangleF(0, 0, MinWidth(), Height());
            labelFont = new Font(labelFont.FontFamily,
                labelFont.Size / GH_GraphicsUtil.UiScale, labelFont.Style);
        }

        public void AddButton(ToggleButtonUI button)
        {
            button.Owner = Owner;
            button.SelectionType = SelectionType;
            Buttons.Add(button);

            // ラジオボタン形式の時、いずれかの要素が選択された状態にする。
            if (SelectionType == ButtonGroupSelectionType.RadioGroup &&
                SelectedIndex == -1)
                SelectItem(0);
        }

        public override float Height()
        {
            return Buttons.Select(x => x.Height()).DefaultIfEmpty(0).Max();
        }

        public override float MinWidth()
        {
            IEnumerable<ToggleButtonUI> active_buttons = Buttons;

            if (SpacingType == ButtonGroupSpacingType.Compact)
                // Spacing Compact
                return active_buttons.Sum(x => x.MinWidth());
            else
            {
                // Spacing Uniform
                float max_width = active_buttons.Max(x => x.MinWidth());
                return max_width * active_buttons.Count();
            }

        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                foreach (var ui in Buttons)
                    ui.Render(canvas, graphics, channel);
            }

        }

        public override void UpdateLayout()
        {
            base.UpdateLayout();

            var base_pos = ContentBounds.Location;
            var base_width = ContentBounds.Width;

            float uniform_width = base_width / Buttons.Count;
            float base_gap = (base_width - MinWidth()) / Buttons.Count;
            foreach (var ui in Buttons)
            {
                float msize = 0;
                if (SpacingType == ButtonGroupSpacingType.Compact)
                    msize = ui.MinWidth() + base_gap;
                else
                    msize = uniform_width;

                ui.Bounds = new RectangleF(base_pos, new SizeF(msize, ui.Height()));
                base_pos.X += msize;
                ui.UpdateLayout();
            }
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
                foreach (var ui in Buttons)
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
                if (mouseOver)
                {
                    mouseOver = false;
                    return new UIResponse(GH_ObjectResponse.Release);
                }

                foreach (var ui in Buttons)
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
                ToggleButtonUI ui = (ToggleButtonUI)ActiveObject;
                int pre_idx = SelectedIndex;
                int clicked_idx = Buttons.IndexOf(ui);

                response = ui.RespondToMouseUp(sender, e);
                if (response.Response == GH_ObjectResponse.Release)
                {
                    if (SelectionType == ButtonGroupSelectionType.Multiple)
                    {
                        OnCheckedChanged();
                        this.Owner.OnDisplayExpired();
                        this.Owner.Owner.RecordUndoEvent("ButtonToggleSelect",
                            new ButtonTogglesUndoAction(pre_idx, clicked_idx, this));
                    }
                    else if (pre_idx != clicked_idx) // RadioGroup
                    {
                        SelectItem(clicked_idx);
                        this.Owner.OnDisplayExpired();
                        this.Owner.Owner.RecordUndoEvent("ButtonToggleSelect",
                            new ButtonTogglesUndoAction(pre_idx, clicked_idx, this));
                    }

                    ActiveObject = null;
                }
            }
            else
            {
                foreach (var ui in Buttons)
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

        private void OnCheckedChanged()
        {
            CheckedChanged?.Invoke();
        }

        public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
        {
            if (TooltipEnabled && IsTooltipRegion(canvasPoint) && TooltipData != null)
            {
                if (TooltipData?.Description is not null) e.Description = TooltipData?.Description;
                if (TooltipData?.Diagram is not null) e.Diagram = TooltipData?.Diagram;
                if (TooltipData?.Icon is not null) e.Icon = TooltipData?.Icon;
                if (TooltipData?.Text is not null) e.Text = TooltipData?.Text;
                if (TooltipData?.Title is not null) e.Title = TooltipData?.Title;
            }

            foreach (ToggleButtonUI ui in Buttons)
                ui.SetupTooltip(canvasPoint, e);
        }

        public override bool Write(GH_IWriter writer)
        {
            // boolean配列をシリアライズ
            byte[] byteArray = MessagePackSerializer.Serialize(Toggles);
            writer.SetByteArray(UniqueName, byteArray);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            // boolean配列のデシリアライズ
            //BinaryFormatter formatter = new BinaryFormatter();
            {
                byte[] byteArray = reader.GetByteArray(UniqueName);
                bool[] bools = MessagePackSerializer.Deserialize<bool[]>(byteArray);
                //MemoryStream ms = new MemoryStream(byteArray);
                //bool[] bools = (bool[])formatter.Deserialize(ms);
                for (int i = 0; i < bools.Length; i++)
                    Buttons[i].Checked = bools[i];
            }
            return true;
        }


        private class ButtonTogglesUndoAction : IGH_UndoAction
        {
            private int previous_id;
            private int select_id;
            private ButtonTogglesUI ui;

            public ButtonTogglesUndoAction(int _preview_id, int _current_id, ButtonTogglesUI _ui)
            {
                previous_id = _preview_id;
                select_id = _current_id;
                ui = _ui;
            }

            public bool ExpiresSolution => false;

            public bool ExpiresDisplay => true;

            public GH_UndoState State { get; set; } = GH_UndoState.undo;

            public bool Read(GH_IReader reader)
                => throw new NotImplementedException();

            public void Redo(GH_Document doc)
            {
                ui.SelectItem(select_id); // value item
                //ui.SelectChanged?.Invoke();
                ui.Owner.OnDisplayExpired();

                // Redoを実行したので次に動作するとしたらUndo
                State = GH_UndoState.undo;
            }

            public void Undo(GH_Document doc)
            {
                if (ui.SelectionType == ButtonGroupSelectionType.Multiple)
                    ui.SelectItem(select_id); // value item
                else
                    ui.SelectItem(previous_id); // switch item

                //ui.SelectChanged?.Invoke();
                ui.Owner.OnDisplayExpired();

                // Undoを実行したので次に動作するとしたらRedo
                State = GH_UndoState.redo;
            }

            public bool Write(GH_IWriter writer)
                => throw new NotImplementedException();
        } // class ButtonTogglesUndoAction

    } // class ButtonTogglesUI
}
