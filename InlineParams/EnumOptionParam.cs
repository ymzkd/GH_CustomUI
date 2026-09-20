using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GH_CustomUI
{
    /// <summary>
    /// 入力値に付随する設定を、enumのドロップダウンで指定するParam。
    /// </summary>
    /// <remarks>
    /// 単位の指定や、入力に与える属性の選択を想定している。
    /// 設定はenumの名前で保存するため、後からenumの並びを変えても値がずれない。
    /// </remarks>
    /// <typeparam name="TGoo">Paramが扱う入力値のGoo型</typeparam>
    /// <typeparam name="TEnum">設定の選択肢</typeparam>
    public class EnumOptionParam<TGoo, TEnum> : GH_InlineParam<TGoo>
        where TGoo : class, IGH_Goo
        where TEnum : struct, Enum
    {
        private const string StateKey = "InlineOption";

        private static readonly Guid s_guid = CreateDeterministicGuid(
            "GH_CustomUI.EnumOptionParam+" + typeof(TGoo).FullName + "+" + typeof(TEnum).FullName);

        private readonly List<TEnum> m_choices;
        private readonly InlineDropDownUI m_ui;

        private TEnum m_option;

        public EnumOptionParam(string name, string nickName, string description,
            TEnum defaultOption = default, float uiWidth = 0f)
            : base(name, nickName, description, "Params", "Primitive")
        {
            m_choices = Enum.GetValues(typeof(TEnum)).Cast<TEnum>().ToList();
            m_option = defaultOption;

            m_ui = new InlineDropDownUI
            {
                Items = m_choices.Select(v => v.ToString()).ToList(),
                PreferredWidth = uiWidth,
                SelectedIndexProvider = () => m_choices.IndexOf(m_option),
                CommitIndex = index =>
                {
                    if (index >= 0 && index < m_choices.Count) Option = m_choices[index];
                }
            };
        }

        /// <summary>UIで選ばれている設定。入力値とは別に保持される。</summary>
        public TEnum Option
        {
            get { return m_option; }
            set { SetInlineState(m_option, value, v => m_option = v, $"Set {NickName}"); }
        }

        public override Guid ComponentGuid => s_guid;

        public override GH_UIParts InlineUI => m_ui;

        protected override bool WriteInlineState(GH_IWriter writer)
        {
            writer.SetString(StateKey, m_option.ToString());
            return true;
        }

        protected override bool ReadInlineState(GH_IReader reader)
        {
            string stored = null;
            if (!reader.TryGetString(StateKey, ref stored)) return true;

            if (Enum.TryParse(stored, out TEnum parsed) && m_choices.Contains(parsed))
                m_option = parsed;

            return true;
        }
    } // class EnumOptionParam<TGoo, TEnum>
}
