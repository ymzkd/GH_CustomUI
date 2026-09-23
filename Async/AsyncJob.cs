using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GH_CustomUI
{
    /// <summary>
    /// <see cref="GH_AsyncComponent{TJob}"/> が扱う1反復ぶんの作業単位の基底。
    /// 入力の読み取り(GHスレッド)で生成し、計算(バックグラウンドスレッド)を経て、
    /// 出力(GHスレッド)で消費する。派生クラスに入力値と計算結果を持たせる。
    /// </summary>
    /// <remarks>
    /// 計算で触れるのはこのオブジェクトが持つデータだけになるよう、
    /// GH の入力やドキュメントに依存する処理は読み取りの時点で済ませておく。
    /// </remarks>
    public class AsyncJob
    {
        private readonly List<KeyValuePair<GH_RuntimeMessageLevel, string>> _messages
            = new List<KeyValuePair<GH_RuntimeMessageLevel, string>>();

        /// <summary>
        /// 読み取り・計算の過程で溜めたメッセージ。出力パスでは solution が
        /// やり直されて既存のメッセージが消えるため、ここに溜めて出力時にまとめて出す。
        /// </summary>
        public IReadOnlyList<KeyValuePair<GH_RuntimeMessageLevel, string>> Messages => _messages;

        /// <summary>メッセージを追加する。計算中のバックグラウンドスレッドからも呼べる</summary>
        public void AddMessage(GH_RuntimeMessageLevel level, string text)
        {
            lock (_messages)
                _messages.Add(new KeyValuePair<GH_RuntimeMessageLevel, string>(level, text));
        }
    }

    /// <summary>
    /// 計算側に渡す実行環境。キャンセルの検知と進捗の通知に使う。
    /// 同期実行時は、キャンセルされず進捗も捨てるものが渡される。
    /// </summary>
    public sealed class AsyncJobContext
    {
        /// <summary>同期実行用(キャンセルなし・進捗通知なし)</summary>
        internal static readonly AsyncJobContext Synchronous
            = new AsyncJobContext(CancellationToken.None, null);

        private readonly Action<double> _reportProgress;

        internal AsyncJobContext(CancellationToken token, Action<double> reportProgress)
        {
            Token = token;
            _reportProgress = reportProgress;
        }

        /// <summary>
        /// キャンセル検知用のトークン。
        /// 計算の区切りごとに <see cref="IsCancellationRequested"/> を確かめて抜ける使い方を想定している。
        /// </summary>
        public CancellationToken Token { get; }

        public bool IsCancellationRequested => Token.IsCancellationRequested;

        /// <summary>
        /// 進捗率(0.0-1.0)を通知する。表示は間引かれるため、細かく呼んでもよい。
        /// </summary>
        public void ReportProgress(double ratio) => _reportProgress?.Invoke(ratio);
    }
}
