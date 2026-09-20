using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;

namespace GH_CustomUI
{
    /// <summary>
    /// 入力値に付随するオンオフの設定を、ラジオボタン風の切り替えで指定するParam。
    /// </summary>
    /// <typeparam name="TGoo">Paramが扱う入力値のGoo型</typeparam>
    public class BoolOptionParam<TGoo> : GH_InlineParam<TGoo>
        where TGoo : class, IGH_Goo
    {
        private const string StateKey = "InlineOption";

        private static readonly Guid s_guid = CreateDeterministicGuid(
            "GH_CustomUI.BoolOptionParam+" + typeof(TGoo).FullName);

        private readonly InlineToggleUI m_ui;

        private bool m_option;

        public BoolOptionParam(string name, string nickName, string description,
            bool defaultOption = false, float uiWidth = 0f)
            : base(name, nickName, description, "Params", "Primitive")
        {
            m_option = defaultOption;

            m_ui = new InlineToggleUI
            {
                ValueProvider = () => m_option,
                CommitValue = value => Option = value
            };

            if (uiWidth > 0f) m_ui.PreferredWidth = uiWidth;
        }

        /// <summary>UIで切り替えられている設定。入力値とは別に保持される。</summary>
        public bool Option
        {
            get { return m_option; }
            set { SetInlineState(m_option, value, v => m_option = v, $"Set {NickName}"); }
        }

        public override Guid ComponentGuid => s_guid;

        public override GH_UIParts InlineUI => m_ui;

        protected override bool WriteInlineState(GH_IWriter writer)
        {
            writer.SetBoolean(StateKey, m_option);
            return true;
        }

        protected override bool ReadInlineState(GH_IReader reader)
        {
            bool stored = m_option;
            if (reader.TryGetBoolean(StateKey, ref stored))
                m_option = stored;

            return true;
        }
    } // class BoolOptionParam<TGoo>
}
