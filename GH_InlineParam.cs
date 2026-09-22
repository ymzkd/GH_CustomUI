using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Undo;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;

namespace GH_CustomUI
{
    /// <summary>
    /// 入力値とは別に、インラインUIで指定する設定を持つParamの基底。
    /// </summary>
    /// <remarks>
    /// 単位や属性のように「入力値に付随する指定」をUIで与えるための土台。
    /// 入力値そのものはPersistentDataとWireに任せ、設定はParam自身のフィールドに持つ。
    /// 保存はParamの<see cref="Write"/>/<see cref="Read"/>で行うため、
    /// Component所属のままでも設定はparam_inputチャンクへ同梱され、
    /// .ghの保存・Copy/Paste・Extract parameterで入力値と一緒に運ばれる。
    /// </remarks>
    /// <typeparam name="T">Paramが扱う入力値のGoo型</typeparam>
    public abstract class GH_InlineParam<T> : GH_PersistentParam<T>, IInlineParamUI
        where T : class, IGH_Goo
    {
        protected GH_InlineParam(string name, string nickName, string description,
            string category, string subCategory)
            : base(name, nickName, description, category, subCategory)
        {
            Access = GH_ParamAccess.item;
        }

        /// <summary>UI本体。派生クラスが生成したパーツを返す。</summary>
        public abstract GH_UIParts InlineUI { get; }

        public virtual SizeF PreferredUiSize
            => InlineUI == null ? SizeF.Empty : new SizeF(InlineUI.MinWidth(), InlineUI.Height());

        /// <summary>
        /// UIが扱うのは入力値ではなく設定なので、Sourceが接続されていても操作できる。
        /// 配線された値に単位や属性を与えるのが主な用途のため。
        /// </summary>
        public virtual bool InlineUIEnabled => true;

        public virtual void LayoutInlineUI(RectangleF uiBounds)
        {
            if (InlineUI == null) return;

            InlineUI.Enable = InlineUIEnabled;
            InlineUI.Bounds = uiBounds;
            InlineUI.UpdateLayout();
        }

        /// <summary>Paramの単独配置はPoCの対象外なのでパレットには出さない。</summary>
        public override GH_Exposure Exposure => GH_Exposure.hidden;

        protected override GH_GetterResult Prompt_Singular(ref T value)
            => GH_GetterResult.cancel;

        protected override GH_GetterResult Prompt_Plural(ref List<T> values)
            => GH_GetterResult.cancel;

        // ------------------------------------------------------------------
        //  設定のシリアライズ
        // ------------------------------------------------------------------

        /// <summary>UIで指定された設定を書き出す。</summary>
        protected abstract bool WriteInlineState(GH_IWriter writer);

        /// <summary>
        /// UIで指定された設定を読み込む。
        /// 保存されていない場合は既定値のままとし、読み込み全体は失敗させない。
        /// </summary>
        protected abstract bool ReadInlineState(GH_IReader reader);

        // &&で繋ぐと基底が失敗した時点で設定の読み書きごと飛ばされるため、
        // 双方を必ず実行してから結果を合成する。
        public override bool Write(GH_IWriter writer)
        {
            bool written = base.Write(writer);
            return WriteInlineState(writer) && written;
        }

        public override bool Read(GH_IReader reader)
        {
            bool read = base.Read(reader);
            return ReadInlineState(reader) && read;
        }

        // ------------------------------------------------------------------
        //  設定の変更
        // ------------------------------------------------------------------

        /// <summary>
        /// Undo登録つきで設定を差し替える。UIからの変更はすべてここを通す。
        /// </summary>
        /// <param name="current">現在値</param>
        /// <param name="value">新しい値</param>
        /// <param name="apply">フィールドへ代入するデリゲート</param>
        /// <param name="undoName">Undoメニューに出る名前</param>
        /// <returns>値が変わったらtrue</returns>
        protected bool SetInlineState<TState>(TState current, TState value,
            Action<TState> apply, string undoName)
        {
            if (EqualityComparer<TState>.Default.Equals(current, value)) return false;

            RecordUndoEvent(undoName, new InlineStateUndoAction<TState>(this, apply, current, value));

            apply(value);
            OnPingDocument()?.Modified();
            ExpireSolution(true);
            return true;
        }

        // ------------------------------------------------------------------
        //  設定の適用(任意)
        // ------------------------------------------------------------------

        /// <summary>
        /// 収集した入力値へ設定を適用するかどうか。
        /// falseの間はデータの走査自体を行わない。
        /// </summary>
        protected virtual bool AppliesInlineState => false;

        /// <summary>
        /// 収集した入力値に設定を適用する。
        /// </summary>
        /// <remarks>
        /// 既定では何もしないので、設定はプロパティとして公開されるだけとなり、
        /// 使うかどうかはComponentのSolveInstance側の判断になる。
        /// 単位換算のように下流へ正規化した値を流したい場合だけ、
        /// <see cref="AppliesInlineState"/>と合わせてoverrideする。
        /// </remarks>
        /// <returns>変換したらtrue、素通しならfalse</returns>
        protected virtual bool TryApplyInlineState(T item, out T converted)
        {
            converted = item;
            return false;
        }

        protected override void OnVolatileDataCollected()
        {
            base.OnVolatileDataCollected();

            // UIの有無は表示の話なので、値の変換条件には混ぜない
            if (!AppliesInlineState) return;

            foreach (List<T> branch in m_data.Branches)
            {
                for (int i = 0; i < branch.Count; i++)
                {
                    if (branch[i] == null) continue;

                    if (TryApplyInlineState(branch[i], out T converted))
                        branch[i] = converted;
                }
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// 閉じたジェネリック型ごとに安定した一意のGUIDを型名から作る。
        /// </summary>
        protected static Guid CreateDeterministicGuid(string key)
        {
            using (MD5 md5 = MD5.Create())
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(key)));
        }

        private class InlineStateUndoAction<TState> : IGH_UndoAction
        {
            private readonly IGH_Param m_param;
            private readonly Action<TState> m_apply;
            private readonly TState m_before;
            private readonly TState m_after;

            public InlineStateUndoAction(IGH_Param param, Action<TState> apply,
                TState before, TState after)
            {
                m_param = param;
                m_apply = apply;
                m_before = before;
                m_after = after;
            }

            public bool ExpiresSolution => true;

            public bool ExpiresDisplay => true;

            public GH_UndoState State { get; set; } = GH_UndoState.undo;

            public void Undo(GH_Document doc)
            {
                Restore(m_before);
                State = GH_UndoState.redo;
            }

            public void Redo(GH_Document doc)
            {
                Restore(m_after);
                State = GH_UndoState.undo;
            }

            /// <summary>
            /// 設定を戻したうえでParamを失効させる。ExpiresSolutionは
            /// 「解を作り直すか」の宣言でしかないため、どのオブジェクトを
            /// 再計算するかはここで明示する必要がある。
            /// </summary>
            private void Restore(TState value)
            {
                m_apply(value);
                m_param.ExpireSolution(false);
                m_param.Attributes?.ExpireLayout();
            }

            public bool Read(GH_IReader reader) => throw new NotImplementedException();

            public bool Write(GH_IWriter writer) => throw new NotImplementedException();
        } // class InlineStateUndoAction
    } // class GH_InlineParam<T>
}
