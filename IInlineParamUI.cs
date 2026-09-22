using System.Drawing;

namespace GH_CustomUI
{
    /// <summary>
    /// Componentに所属するInput Paramが、自前のインラインUIを持つことを表すインターフェース。
    /// </summary>
    /// <remarks>
    /// UIの意味・状態・処理はParam側に置き、Canvasへの描画とイベント配送だけを
    /// ホストとなる<see cref="GH_UIComponentAttributes"/>が担当する。
    /// GH1ではComponent内Paramの描画を最終的にGH_ComponentAttributesが行うため、
    /// Param自身のAttributesだけではUIを自己完結させられないことによる分担。
    /// </remarks>
    public interface IInlineParamUI
    {
        /// <summary>
        /// UI本体。描画とマウス/キーイベントはこのパーツへ委譲される。
        /// </summary>
        GH_UIParts InlineUI { get; }

        /// <summary>
        /// Paramが申告する希望サイズ。最終的な配置領域は親Attributesが決める。
        /// </summary>
        /// <remarks>
        /// Attributes.Boundsを希望サイズの受け渡しに使わないこと。
        /// PreferredUiSize = 希望サイズ / Attributes.Bounds = 割り当てられた領域。
        /// </remarks>
        SizeF PreferredUiSize { get; }

        /// <summary>
        /// UIを操作できるか。falseの間はグレー表示となり、イベントも配送されない。
        /// </summary>
        /// <remarks>
        /// UIが扱うのは入力値そのものではなく、それに付随する設定(単位や属性)なので、
        /// Sourceが接続されていても操作できるのが既定。
        /// </remarks>
        bool InlineUIEnabled { get; }

        /// <summary>
        /// 親Attributesが確定させた最終的なUI矩形を通知する。
        /// </summary>
        void LayoutInlineUI(RectangleF uiBounds);
    }
}
