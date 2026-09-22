using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// DropDownUI
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DropDownUI<T> : GH_UIParts
    {
        // Action
        public Action ValueChanged { get; set; }

        //Properties
        // Data
        public int SelectedIndex { get; set; }
        public List<T> DropDownList;
        public T SelectedItem { get { return DropDownList[SelectedIndex]; } }

        public bool IsMenuExpand { get; set; }

        private int focusedIndex = -1;

        // UI
        public float MarginVertical = 3f;
        public float MarginHorizontal = 4f;
        public Brush BoxFillBrush { get; set; } = Brushes.LightGray;

        private RectangleF SelectorBounds
        {
            get { return RectangleF.Inflate(this.Bounds, -MarginHorizontal, -MarginVertical); }
        }

        private RectangleF DropdownBound
        {
            get
            {
                return new RectangleF(
                    SelectorBounds.X, SelectorBounds.Bottom,
                    SelectorBounds.Width,
                    SelectorBounds.Height * DropDownList.Count
                    );
            }
        }

        private List<RectangleF> DropdownBounds
        {
            get
            {
                List<RectangleF> bounds = new List<RectangleF>();
                for (int i = 0; i < DropDownList.Count; i++)
                {
                    RectangleF rec = new RectangleF(
                        SelectorBounds.X,
                        SelectorBounds.Bottom + SelectorBounds.Height * i,
                        SelectorBounds.Width,
                        SelectorBounds.Height);
                    bounds.Add(rec);
                }
                return bounds;
            }
        }

        private Font label_font = GH_FontServer.Standard;

        public DropDownUI() : this(new T[] { }) { }

        // Constractor
        public DropDownUI(IEnumerable<T> dropDownList, int selectedIndex = 0)
        {
            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);

            DropDownList = dropDownList.ToList();

            // SelectedIndexがdropDownListの範囲内であることは要チェック？
            SelectedIndex = selectedIndex;
        }

        /// <summary>
        /// 指定した値と一致するアイテムを選択する
        /// </summary>
        /// <param name="item">選択するアイテム</param>
        /// <returns>アイテムが見つかった場合はtrue</returns>
        public bool SetSelectedItem(T item)
        {
            int index = DropDownList.IndexOf(item);
            if (index >= 0)
            {
                SelectedIndex = index;
                return true;
            }
            return false;
        }

        private void OnValueChanged(int pre, int post)
        {
            NotifyDocumentModified();

            Owner.Owner.RecordUndoEvent("Selector",
                new DropDownUndoAction<T>(pre, post, this));

            SelectedIndex = post;
            ValueChanged?.Invoke();

        }

        // Methods
        public override float Height()
        {
            return 30f;
        }

        // 要素が何もないという場合もカバーするか？
        //  -> 最低幅のようなものを与える
        public override float MinWidth()
        {
            float tw = MaxTextWidth(DropDownList, label_font) + MarginHorizontal;
            float bw = Height();
            return tw + bw;
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            // Label Format Setting
            StringFormat format = new StringFormat();
            format.Alignment = StringAlignment.Near;
            format.LineAlignment = StringAlignment.Center;

            if (channel == GH_CanvasChannel.Objects)
            {
                Pen box_pen = new Pen(Color.DarkGray, 2f);
                //Brush box_fill = new SolidBrush(Color.LightGray);

                RectangleF sb = SelectorBounds;
                graphics.DrawRectangle(box_pen, sb.X, sb.Y, sb.Width, sb.Height);
                graphics.FillRectangle(BoxFillBrush, sb.X, sb.Y, sb.Width, sb.Height);

                float dd_button_size = Height() - 8f;
                RectangleF dd_button_bounds = new RectangleF(Bounds.Right - Height(), Bounds.Y, Height(), Height());
                PointF button_center = new PointF(dd_button_bounds.X + Height() * 0.5f, dd_button_bounds.Y + Height() * 0.5f);
                DrawDropDownButton(graphics, button_center, Color.Red, (int)dd_button_size);

                if (SelectedIndex < 0 || DropDownList.Count == 0)
                    graphics.DrawString("---", label_font, Brushes.Black, SelectorBounds, format);
                else if(SelectedIndex >= DropDownList.Count)
                    graphics.DrawString("###", label_font, Brushes.Red, SelectorBounds, format);
                else
                    graphics.DrawString(DropDownList[SelectedIndex].ToString(), label_font, Brushes.Black, SelectorBounds, format);

            }

            if (channel == GH_CanvasChannel.Overlay && IsMenuExpand)
            {
                Pen box_pen = new Pen(Color.DarkGray);
                //Brush box_fill = new SolidBrush(Color.LightGray);

                RectangleF sb = DropdownBound;
                graphics.DrawRectangle(box_pen, sb.X, sb.Y, sb.Width, sb.Height);
                graphics.FillRectangle(BoxFillBrush, sb.X, sb.Y, sb.Width, sb.Height);

                for (int i = 0; i < DropDownList.Count; i++)
                {

                    Brush focused_fill = new SolidBrush(Color.Silver);
                    if (i == focusedIndex)
                    {
                        RectangleF fbox = DropdownBounds[i];
                        graphics.FillRectangle(focused_fill, fbox.X, fbox.Y, fbox.Width, fbox.Height);
                    }

                    // ラベルの描画
                    graphics.DrawString(DropDownList[i].ToString(), label_font, Brushes.Black, DropdownBounds[i], format);
                }
            }
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // 以降は左ボタンの時だけ処理
            if (e.Button != MouseButtons.Left)
                return UIResponse.Ignore;

            if (SelectorBounds.Contains(e.CanvasLocation))
            {
                // Selector部分をクリック時の処理
                IsMenuExpand = !IsMenuExpand;

                if (IsMenuExpand) // 展開時
                {
                    // DropDown部分のCanvas上で再レンダリング
                    return new UIResponse(GH_ObjectResponse.Capture, true);
                }
                else // Selector部分でCloseしたとき
                {
                    return new UIResponse(GH_ObjectResponse.Release, true);
                }
            }
            else if (IsMenuExpand)
            {
                // Dropdown展開中でSelected部分以外のMouseUp時

                // Dropdown部分クリック判定
                List<RectangleF> ddbs = DropdownBounds;
                for (int i = 0; i < DropDownList.Count; i++)
                {
                    if (ddbs[i].Contains(e.CanvasLocation))
                    {
                        // Dropdown部分クリック時の処理
                        if (SelectedIndex != i)
                        {
                            // SelectItemを更新
                            OnValueChanged(SelectedIndex, i);
                        }

                        IsMenuExpand = false;
                        return new UIResponse(GH_ObjectResponse.Release, true);
                    }
                }

                // Dropdown以外の部分のクリック処理
                // Dropdownを閉じ、Canvas上で再レンダリング
                IsMenuExpand = false;
                return new UIResponse(GH_ObjectResponse.Release, true);
            }

            // 左クリック・DropDown非展開時・
            // Selector以外をクリック時
            return UIResponse.Ignore;
        }

        public override UIResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            // Dropdownが未展開
            if (!IsMenuExpand) return UIResponse.Ignore;

            // Dropdown部分ホバー時の処理
            List<RectangleF> ddbs = DropdownBounds;
            for (int i = 0; i < DropDownList.Count; i++)
            {
                if (ddbs[i].Contains(e.CanvasLocation))
                {
                    // いずれかのDropdownにフォーカス
                    focusedIndex = i;
                    return new UIResponse(GH_ObjectResponse.Handled, true);
                    //break;
                }
            }

            // 上のループでヒットしなかったら要素のホバー無し
            focusedIndex = -1;
            return UIResponse.Ignore;
        }

        public override UIResponse RespondToKeyDown(GH_Canvas sender, KeyEventArgs e)
        {
            if (IsMenuExpand && (e.KeyCode == Keys.Escape))
            {
                IsMenuExpand = false;
                return new UIResponse(GH_ObjectResponse.Release);
            }
            return base.RespondToKeyDown(sender, e);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32(UniqueName, SelectedIndex);
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            // 保存されていない場合は既定の選択を維持する
            int idx_ref = SelectedIndex;
            if (!reader.TryGetInt32(UniqueName, ref idx_ref)) return false;

            SelectedIndex = idx_ref;
            return true;
        }

        private class DropDownUndoAction<T> : IGH_UndoAction
        {
            private int preview_id;
            private int current_id;
            private DropDownUI<T> ui;

            public DropDownUndoAction(int _preview_id, int _current_id, DropDownUI<T> _ui)
            {
                preview_id = _preview_id;
                current_id = _current_id;
                ui = _ui;
            }

            public bool ExpiresSolution => false;

            public bool ExpiresDisplay => true;

            public GH_UndoState State { get; set; } = GH_UndoState.undo;

            public bool Read(GH_IReader reader)
                => throw new NotImplementedException();

            public void Redo(GH_Document doc)
            {
                ui.SelectedIndex = current_id;
                ui.ValueChanged?.Invoke();

                // Redoを実行したので次に動作するとしたらUndo
                State = GH_UndoState.undo;
            }

            public void Undo(GH_Document doc)
            {
                ui.SelectedIndex = preview_id;
                ui.ValueChanged?.Invoke();

                // Undoを実行したので次に動作するとしたらRedo
                State = GH_UndoState.redo;
            }

            public bool Write(GH_IWriter writer)
                => throw new NotImplementedException();
        } // class DropDownUndoAction<T>
    } // class DropDownUI<T>
}
