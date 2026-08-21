using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Undo;
using MessagePack;
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GH_CustomUI
{
    public enum SelectorButtonStyle
    {
        Round, Checkbox
    }

    /// <summary>
    /// オンオフの状態を持った複数の要素を持つUI
    /// RadioButton形式か、CheckBox形式かプロパティで切替可能
    /// </summary>
    public class GroupTogglesUI : GH_UIParts
    {
        public Action SelectChanged { get; set; } = () => { };

        /// <summary>
        /// ラベルの行数
        /// </summary>
        public int Rows { get; set; } = 1;

        /// <summary>
        /// ラベルの列数
        /// </summary>
        public int Columns
        {
            get
            {
                if (Rows == 1) return Count;

                int cols = Count / Rows;
                cols += (Count % Rows == 0) ? 0 : 1;
                return cols;
            }
        }

        public string[] Labels;
        public bool[] Toggles;
        public bool MultiSelect { get; set; }
        public SelectorButtonStyle ButtonStyle;

        /// <summary>ラベルとボタンの並び</summary>
        public LabelOrientation Orientation { get; set; } = LabelOrientation.Vertical;

        /// <summary>横並び時のラベルとボタンの間隔</summary>
        public float LabelSpacing = 4f;
        //public ButtonGroupSelectionType SelectionType
        //    => MultiSelect?ButtonGroupSelectionType.Multiple:ButtonGroupSelectionType.RadioGroup;

        public float Margin = 2f;

        public int Count { get { return Labels.Length; } }

        public int SelectedIndex { get { return Array.IndexOf(Toggles, true); } }

        private RectangleF[] radio_bounds;
        private RectangleF[] label_bounds;

        private Font label_font = GH_FontServer.Standard;
        private float radio_button_size = 10f;

        private float label_width;
        private float label_height;
        private float column_space
        {
            get { return (Bounds.Width - Margin * 2 - column_width * Columns) / Columns; }
        }

        /// <summary>1要素あたりの幅（並びによってラベルとボタンの積み方が変わる）</summary>
        private float column_width
            => Orientation == LabelOrientation.Horizontal
                ? label_width + LabelSpacing + radio_button_size
                : Math.Max(label_width, radio_button_size);

        /// <summary>1行あたりの高さ（上下のMarginを含む）</summary>
        private float row_height
            => Margin * 2 + (Orientation == LabelOrientation.Horizontal
                ? Math.Max(label_height, radio_button_size)
                : label_height + radio_button_size);

        /// <summary>
        /// Initialize GroupTogglesUI.
        /// 複数選択と、ラジオボタン形式の単一選択を同じクラスとしたため少し入力が不便
        /// </summary>
        /// <param name="labels"></param>
        /// <param name="toggles"></param>
        /// <param name="multiSelect"></param>
        public GroupTogglesUI(string[] labels, bool[] toggles, bool multiSelect = true)
        {
            Labels = labels;
            Toggles = Enumerable.Repeat(false, labels.Length).ToArray();
            ButtonStyle = SelectorButtonStyle.Round;

            label_font = new Font(label_font.FontFamily,
                label_font.Size / GH_GraphicsUtil.UiScale, label_font.Style);
            label_width = MaxTextWidth(Labels, label_font) + 8f;
            label_height = label_font.Height;

            Bounds = new RectangleF(0, 0, MinWidth(), Height());
            radio_bounds = new RectangleF[Count];
            label_bounds = new RectangleF[Count];

            MultiSelect = multiSelect;
            if (MultiSelect)
            {
                for (int i = 0; i < toggles.Length; i++)
                    Toggles[i] = toggles[i];
            }
            else
            {
                int selected_id = Array.IndexOf(toggles, true);
                if (selected_id < 0) selected_id = 0;

                Toggles[selected_id] = true;
            }
        }

        public override float Height()
            => Margin + row_height * Rows;

        public override float MinWidth()
            => Margin * (Columns + 1) + column_width * Columns;


        private class GroupTogglesUndoAction : IGH_UndoAction
        {
            private int previous_id;
            private int select_id;
            private GroupTogglesUI ui;

            public GroupTogglesUndoAction(int _preview_id, int _current_id, GroupTogglesUI _ui)
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
                ui.SelectItem(select_id);
                ui.SelectChanged?.Invoke();
                ui.Owner.OnDisplayExpired();

                // Redoを実行したので次に動作するとしたらUndo
                State = GH_UndoState.undo;
            }

            public void Undo(GH_Document doc)
            {
                if (ui.MultiSelect)
                    ui.SelectItem(select_id); // value item
                else
                    ui.SelectItem(previous_id); // switch item

                ui.SelectChanged?.Invoke();
                ui.Owner.OnDisplayExpired();

                // Undoを実行したので次に動作するとしたらRedo
                State = GH_UndoState.redo;
            }

            public bool Write(GH_IWriter writer)
                => throw new NotImplementedException();
        }

        public override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            if (channel == GH_CanvasChannel.Objects)
            {
                // Label Format Setting
                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;


                for (int i = 0; i < Count; i++)
                {
                    // ラベルの描画
                    graphics.DrawString(Labels[i], label_font, Brushes.Black, label_bounds[i], format);

                    // Radio Buttonの描画
                    if (ButtonStyle == SelectorButtonStyle.Round)
                    {
                        DrawRadioButton(graphics, radio_bounds[i], Toggles[i]);
                    }
                    else
                    {
                        DrawCheckbox(graphics, radio_bounds[i], Toggles[i]);
                    }
                }
            }
        }

        public override void UpdateLayout()
        {
            float radio_height_gage = row_height;

            if (Orientation == LabelOrientation.Horizontal)
            {
                // 各要素についてラベルとボタンを横に並べる
                float cell_x0 = Margin + Bounds.X + column_space * 0.5f;
                float cell_y0 = Margin + Bounds.Y;
                float cell_height = radio_height_gage - Margin * 2;

                for (int i = 0; i < Count; i++)
                {
                    int row = i / Columns;
                    int col = i % Columns;

                    float cell_x = cell_x0 + (column_width + column_space) * col;
                    float center_y = cell_y0 + row * radio_height_gage + cell_height / 2f;

                    label_bounds[i].X = cell_x;
                    label_bounds[i].Y = center_y - label_height / 2f;
                    label_bounds[i].Width = label_width;
                    label_bounds[i].Height = label_height;

                    radio_bounds[i].X = cell_x + label_width + LabelSpacing;
                    radio_bounds[i].Y = center_y - radio_button_size / 2f;
                    radio_bounds[i].Width = radio_button_size;
                    radio_bounds[i].Height = radio_button_size;
                }
                return;
            }

            // Radio ButtonのRectangleFをUpdateする。
            // -> BoundsはCustomAttribute側で更新されている
            float x0 = Margin + Bounds.X + column_space * 0.5f;
            float y0 = Margin + Bounds.Y;
            float x1 = Margin + (column_width - radio_button_size) / 2f + Bounds.X + column_space * 0.5f;
            float y1 = Margin * 2f + label_height + Bounds.Y;

            for (int i = 0; i < Count; i++)
            {
                int row = i / Columns;
                float y_offset = row * radio_height_gage;
                int col = i % Columns;

                float dxi = (column_width + column_space) * col;

                label_bounds[i].X = x0 + dxi;
                label_bounds[i].Y = y0 + y_offset;
                label_bounds[i].Width = column_width;
                label_bounds[i].Height = label_height;

                radio_bounds[i].X = x1 + dxi;
                radio_bounds[i].Y = y1 + y_offset;
                radio_bounds[i].Width = radio_button_size;
                radio_bounds[i].Height = radio_button_size;
            }
        }

        public override UIResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // CheckBoxクリック時
                int idx = CheckClick(e.CanvasLocation);
                if (idx >= 0)
                {
                    int preidx = SelectItem(idx);
                    if (preidx == -1)
                        return new UIResponse(GH_ObjectResponse.Ignore, false);

                    OnSelectChanged(preidx, idx);
                    //if (MultiSelect)
                    //    OnSelectChanged(idx);
                    //else
                    //    OnSelectChanged(preidx, idx);

                    SelectChanged?.Invoke();
                }
            }

            return UIResponse.Ignore;
        }

        /// <summary>
        /// マウスクリック時の座標によって値の更新を確認
        /// </summary>
        /// <param name="locate">マウスクリック時の座標</param>
        /// <returns>-1の時どの要素もクリックされていない、
        /// その他の場合はクリックされた要素インデックスが返り値となる。</returns>
        private int CheckClick(PointF locate)
        {
            // Radio ButtonのRectangleFのクリック判定とToggles配列の更新
            if (!Bounds.Contains(locate)) return -1;

            for (int i = 0; i < Count; i++)
            {
                if (!radio_bounds[i].Contains(locate)) continue;

                return i;
            }
            return -1;
        }

        /// <summary>
        /// インデックスで指定したアイテムを選択する。
        /// </summary>
        /// <param name="idx"></param>
        /// <returns>以前選択されていたアイテムのインデックス,
        /// 指定により更新されない場合は-1が返される。</returns>
        public int SelectItem(int idx)
        {
            if (MultiSelect)
            {
                // CheckBox形式の時
                Toggles[idx] = !Toggles[idx];
                return idx;
            }
            else
            {
                // RadioButton形式の時
                if (!Toggles[idx])
                {
                    int preidx = Array.IndexOf(Toggles, true);
                    Toggles[idx] = true;
                    Toggles[preidx] = false;
                    return idx;
                }
            }
            return -1;
        }


        /// <summary>
        /// UIがMultiSelectの場合に選択が変更されたときに呼び出されるメソッド。
        /// </summary>
        /// <param name="clicked_idx">クリックされたアイテムのインデックス</param>
        //private void OnSelectChanged(int clicked_idx)
        //{
        //    Owner.Owner.RecordUndoEvent("Multi_Selector",
        //        new MultiSelectUndoAction(clicked_idx, this));
        //}

        /// <summary>
        /// UIがRadioButton形式の場合に選択が変更されたときに呼び出されるメソッド。
        /// </summary>
        /// <param name="pre">以前選択されていたアイテムのインデックス</param>
        /// <param name="post">新しく選択されたアイテムのインデックス</param>
        private void OnSelectChanged(int pre, int post)
        {
            Owner.Owner.RecordUndoEvent("Radio_Selector",
                new GroupTogglesUndoAction(pre, post, this));
        }

        private void DrawRadioButton(Graphics graphics, RectangleF bounds, bool enabled)
        {
            Pen pen = new Pen(Color.Black, 1.5f);
            Brush fill = new SolidBrush(Color.White);
            Brush brush_on = Brushes.Black;
            Brush brush_off = Brushes.White;
            Brush fill_inside = enabled ? brush_on : brush_off;

            RectangleF radio_inside = RectangleF.Inflate(bounds, -2, -2);
            graphics.FillEllipse(fill, bounds);
            graphics.DrawEllipse(pen, bounds);
            graphics.FillEllipse(fill_inside, radio_inside);
        }

        private void DrawCheckbox(Graphics graphics, RectangleF bounds, bool enabled)
        {
            Pen pen = new Pen(Color.Black, 1.5f);
            Brush fill = new SolidBrush(Color.White);
            Brush brush_on = Brushes.Black;
            Brush brush_off = Brushes.White;
            Brush fill_inside = enabled ? brush_on : brush_off;

            // 外枠を描画する
            graphics.FillRectangle(brush_off, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            if (enabled)
            {
                float x = bounds.X;
                float y = bounds.Y;
                float size = bounds.Width;
                Pen check_pen = new Pen(Color.Black, size / 6); // CheckBoxのライン
                // チェックを描画する
                var lines = new[] { new PointF(x + size / 4, y + size / 2),
                            new PointF(x + size / 2, y + 3 * size / 4),
                            new PointF(x + 3 * size / 4, y + size / 4) };
                graphics.DrawLines(check_pen, lines);
            }
        }

        public override bool Write(GH_IWriter writer)
        {
            // boolean配列をシリアライズ
            //BinaryFormatter formatter = new BinaryFormatter();
            //using (MemoryStream ms = new MemoryStream())
            {
                //formatter.Serialize(ms, Toggles);
                //byte[] byteArray = ms.ToArray();
                writer.SetByteArray(UniqueName, MessagePackSerializer.Serialize(Toggles));
                //writer.SetByteArray(UniqueName, byteArray);
            }
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            // boolean配列のデシリアライズ
            byte[] byteArray = reader.GetByteArray(UniqueName);
            bool[] loadedToggles = MessagePackSerializer.Deserialize<bool[]>(byteArray);

            // 配列サイズが異なる場合の互換性処理
            if (loadedToggles.Length == Labels.Length)
            {
                Toggles = loadedToggles;
            }
            else
            {
                // 新しいサイズの配列を作成（デフォルトはtrue）
                Toggles = Enumerable.Repeat(true, Labels.Length).ToArray();

                // 古い値をコピー（可能な範囲で）
                int copyCount = Math.Min(loadedToggles.Length, Labels.Length);
                for (int i = 0; i < copyCount; i++)
                {
                    Toggles[i] = loadedToggles[i];
                }
            }
            return true;
        }

    }
}
