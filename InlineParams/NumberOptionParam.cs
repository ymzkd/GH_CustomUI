using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.Globalization;

namespace GH_CustomUI
{
    /// <summary>
    /// 入力値に付随する数値設定を、値ボックスで指定するParam。
    /// </summary>
    /// <typeparam name="TGoo">Paramが扱う入力値のGoo型</typeparam>
    public class NumberOptionParam<TGoo> : GH_InlineParam<TGoo>
        where TGoo : class, IGH_Goo
    {
        private const string StateKey = "InlineOption";

        private static readonly Guid s_guid = CreateDeterministicGuid(
            "GH_CustomUI.NumberOptionParam+" + typeof(TGoo).FullName);

        private readonly InlineTextFieldUI m_ui;

        private double m_option;

        public NumberOptionParam(string name, string nickName, string description,
            double defaultOption = 0.0,
            double minimum = double.MinValue, double maximum = double.MaxValue,
            string format = "0.###", float uiWidth = 0f)
            : base(name, nickName, description, "Params", "Primitive")
        {
            Minimum = minimum;
            Maximum = maximum;
            DisplayFormat = format;
            if (IsValidOption(defaultOption)) m_option = Clamp(defaultOption);

            m_ui = new InlineTextFieldUI
            {
                TextProvider = () => m_option.ToString(DisplayFormat, CultureInfo.InvariantCulture),
                CommitText = TryCommitText
            };

            // 0のときはウィジェットの既定幅(編集ボックスのボタン分を織り込んだ値)を使う
            if (uiWidth > 0f) m_ui.PreferredWidth = uiWidth;
        }

        public double Minimum { get; set; }

        public double Maximum { get; set; }

        public string DisplayFormat { get; set; }

        /// <summary>UIで入力されている設定。入力値とは別に保持される。</summary>
        public double Option
        {
            get { return m_option; }
            set
            {
                // 不正値では更新しない
                if (!IsValidOption(value)) return;

                SetInlineState(m_option, Clamp(value), v => m_option = v, $"Set {NickName}");
            }
        }

        public override Guid ComponentGuid => s_guid;

        public override GH_UIParts InlineUI => m_ui;

        /// <summary>
        /// 設定として受け付けられる値かどうか。
        /// </summary>
        /// <remarks>
        /// NaNと無限大はここで弾く。<see cref="Clamp"/>が使う
        /// <see cref="Math.Min(double, double)"/>系はNaNをそのまま返すため、
        /// 到達させるとMinimum/Maximumを素通りして保存まで通ってしまう。
        /// </remarks>
        private static bool IsValidOption(double value) => double.IsFinite(value);

        private double Clamp(double value) => Math.Min(Maximum, Math.Max(Minimum, value));

        private bool TryCommitText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            // Anyは桁区切りと通貨記号を通すため、"1,5"が15と読まれてしまう
            if (!double.TryParse(text.Trim(), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double parsed))
                return false;

            // NaNとInfinityはNumberStylesによらずTryParseを通るのでここで弾く
            if (!IsValidOption(parsed)) return false;

            double before = m_option;
            Option = parsed;
            return !before.Equals(m_option);
        }

        protected override bool WriteInlineState(GH_IWriter writer)
        {
            writer.SetDouble(StateKey, m_option);
            return true;
        }

        protected override bool ReadInlineState(GH_IReader reader)
        {
            double stored = m_option;
            if (reader.TryGetDouble(StateKey, ref stored) && IsValidOption(stored))
                m_option = Clamp(stored);

            return true;
        }
    } // class NumberOptionParam<TGoo>
}
