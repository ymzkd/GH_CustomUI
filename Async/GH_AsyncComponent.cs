using GH_IO.Serialization;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GH_CustomUI
{
    /// <summary>
    /// 重い計算をバックグラウンドで行い、キャンバスの操作を止めないコンポーネントの基底。
    /// </summary>
    /// <remarks>
    /// 1回の solution を次の3段階に分けて実装する。
    /// <list type="number">
    /// <item><see cref="CollectJob"/>: 入力を読み取り、ジョブを組み立てる(GHスレッド、反復ごと)</item>
    /// <item><see cref="RunJob"/>: 計算する(バックグラウンドスレッド)</item>
    /// <item><see cref="PublishJob"/>: 結果を出力する(GHスレッド、反復ごと)</item>
    /// </list>
    /// 全反復の読み取りが終わってから計算を始め、計算が終わると solution をやり直して
    /// 出力する(出力パス)。入力が変わると走行中の計算は打ち切られ、その結果は捨てられる。
    /// 計算中は前回の結果を下流に残したままにし、出力パスではじめて下流へ伝播させる。
    ///
    /// 右クリックメニューで同期実行に切り替えられる。最適化ソルバーや Rhino.Compute のように
    /// 同じ solution 内で結果が出ることを前提にする呼び出し元では同期実行にする。
    /// 同期実行では3段階をそのまま続けて行う。
    /// </remarks>
    /// <typeparam name="TJob">1反復ぶんの作業単位</typeparam>
    public abstract class GH_AsyncComponent<TJob> : GH_Component where TJob : AsyncJob
    {
        /// <summary>
        /// 非同期実行の進行状態。
        /// Idle → Collecting(入力読み取り) → Running(バックグラウンド計算)
        /// → Publishing(結果の出力) → Idle と遷移する。
        /// バックグラウンドスレッドから書き込むため volatile。
        /// </summary>
        private enum AsyncPhase { Idle, Collecting, Running, Publishing }

        /// <summary>
        /// 1回のバックグラウンド計算ぶんの進捗。計算を始めるたびに作り直すため、
        /// 打ち切られた古い計算からの通知が新しい計算の表示に混ざらない。
        /// </summary>
        private sealed class ProgressState
        {
            private readonly double[] _ratios;
            private readonly bool _parallel;
            private volatile int _lastIndex;

            public ProgressState(int jobCount, bool parallel)
            {
                _ratios = new double[jobCount];
                _parallel = parallel;
            }

            public void Report(int index, double ratio)
            {
                _ratios[index] = Math.Max(0.0, Math.Min(1.0, ratio));
                _lastIndex = index;
            }

            /// <summary>
            /// 表示用の文字列。逐次実行では何番目のジョブかを添え、
            /// 並列実行では全ジョブの平均を出す。
            /// </summary>
            public string Format()
            {
                int count = _ratios.Length;
                if (count == 0)
                    return null;
                if (count == 1)
                    return _ratios[0].ToString("P0");
                if (_parallel)
                    return _ratios.Average().ToString("P0");

                int index = _lastIndex;
                return $"{index + 1}/{count} {_ratios[index]:P0}";
            }
        }

        private bool _asyncEnabled = true;

        private volatile AsyncPhase _phase = AsyncPhase.Idle;

        /// <summary>_phase / _cts / _generation の遷移を GHスレッドとタスクの間で直列化する</summary>
        private readonly object _stateLock = new object();

        private CancellationTokenSource _cts;

        /// <summary>入力が変わるたびに進む世代番号。古いタスクの結果を捨てるために使う</summary>
        private long _generation;

        /// <summary>このパスで収集したジョブ。反復インデックスと並び順が対応する</summary>
        private readonly List<TJob> _jobs = new List<TJob>();

        /// <summary>
        /// 進捗表示の間引き用タイマ。計算側は進捗を記録するだけにして、
        /// キャンバスの再描画はこのタイマの間隔に抑える(毎回描画するとUIが詰まる)。
        /// </summary>
        private readonly System.Timers.Timer _progressTimer;
        private volatile ProgressState _progress;

        protected GH_AsyncComponent(string name, string nickname, string description,
            string category, string subCategory)
            : base(name, nickname, description, category, subCategory)
        {
            _progressTimer = new System.Timers.Timer(300) { AutoReset = false };
            _progressTimer.Elapsed += (s, e) => DisplayProgress();
        }

        /// <summary>
        /// バックグラウンドで計算するか。右クリックメニューで切り替えられ、定義ファイルに保存される。
        /// 既定値を変える場合は派生クラスのコンストラクタで設定する。
        /// </summary>
        public bool AsyncEnabled
        {
            get => _asyncEnabled;
            protected set => _asyncEnabled = value;
        }

        /// <summary>バックグラウンドで計算中か</summary>
        public bool IsRunning => _phase == AsyncPhase.Running;

        /// <summary>
        /// 複数反復のジョブを並列に計算するか。既定は逐次。
        /// ジョブごとに大きなメモリを抱える計算(行列の因数分解など)では、
        /// 並列にすると反復数ぶん同時に抱えることになるため逐次のままにする。
        /// </summary>
        protected bool RunJobsInParallel { get; set; }

        /// <summary>
        /// バックグラウンド計算のタスク生成オプション。長時間かかる計算では
        /// <see cref="TaskCreationOptions.LongRunning"/> を指定するとスレッドプールを占有しない。
        /// </summary>
        protected TaskCreationOptions TaskCreationOptions { get; set; } = TaskCreationOptions.None;

        /// <summary>
        /// 入力を読み取り、ジョブを組み立てる(GHスレッドで実行する)。
        /// 入力に不備がある場合も null ではなく、メッセージだけを持たせたジョブを返す。
        /// </summary>
        protected abstract TJob CollectJob(IGH_DataAccess DA);

        /// <summary>
        /// 計算本体。非同期実行ではバックグラウンドスレッド、同期実行では GHスレッドから呼ばれる。
        /// GH のオブジェクトには触れず、ジョブが持つデータだけを扱う。
        /// 区切りごとに <see cref="AsyncJobContext.IsCancellationRequested"/> を確かめて抜けること。
        /// 投げられた例外はエラーメッセージとしてジョブに記録される。
        /// </summary>
        protected abstract void RunJob(TJob job, AsyncJobContext context);

        /// <summary>
        /// ジョブの結果を出力する(GHスレッドで実行する)。
        /// ジョブに溜めたメッセージは、この呼び出しの前に基底で出力される。
        /// </summary>
        protected abstract void PublishJob(TJob job, IGH_DataAccess DA);

        /// <summary>
        /// 非同期実行中は下流を無効化しない。無効化してしまうと、計算が終わるまでの間
        /// 後続コンポーネントが空データで再計算されてしまう(結果が一瞬消える)。
        /// 前回の結果を保持したまま計算し、出力パスではじめて下流へ伝播させる。
        /// </summary>
        protected override void ExpireDownStreamObjects()
        {
            if (_asyncEnabled && _phase != AsyncPhase.Publishing)
                return;
            base.ExpireDownStreamObjects();
        }

        /// <summary>
        /// 反復ごとの SolveInstance より前に呼ばれる。入力が変わった solution の始まりなので、
        /// 走行中のタスクをキャンセルして収集をやり直す。
        /// バックグラウンドの完了を受けて走る出力パスでは何もしない(収集結果を保持する)。
        /// 派生クラスで override する場合は必ず base を呼ぶこと。
        /// </summary>
        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();

            lock (_stateLock)
            {
                if (_phase == AsyncPhase.Publishing)
                    return;

                CancelRunningNoLock();
                _jobs.Clear();
                _progress = null;
                _phase = _asyncEnabled ? AsyncPhase.Collecting : AsyncPhase.Idle;
            }
            Message = null;
        }

        /// <summary>
        /// 全反復の収集が終わってからタスクを起動する。
        /// 反復ごとに起動すると、まだ収集中の反復と計算が競合する。
        /// 派生クラスで override する場合は必ず base を呼ぶこと。
        /// </summary>
        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();

            if (_phase == AsyncPhase.Publishing)
            {
                _phase = AsyncPhase.Idle;
                Message = null;
                return;
            }

            if (_phase != AsyncPhase.Collecting)
                return;

            _phase = AsyncPhase.Running;
            StartBackgroundSolve();
        }

        protected sealed override void SolveInstance(IGH_DataAccess DA)
        {
            // バックグラウンドの計算が終わって呼び戻された出力パス
            if (_phase == AsyncPhase.Publishing)
            {
                if (DA.Iteration < _jobs.Count)
                    Publish(_jobs[DA.Iteration], DA);
                return;
            }

            // 同期実行: 読み取り・計算・出力をこのまま続けて行う
            if (!_asyncEnabled)
            {
                TJob job = CollectJob(DA);
                ExecuteJob(job, AsyncJobContext.Synchronous);
                Publish(job, DA);
                return;
            }

            // 非同期実行: ここでは入力の読み取りだけを行う。計算は AfterSolveInstance で起動する
            _jobs.Add(CollectJob(DA));
        }

        /// <summary>
        /// 計算を実行する。バックグラウンドで投げっぱなしにした例外は観測されないため、
        /// メッセージにして出力パスへ渡す。
        /// </summary>
        private void ExecuteJob(TJob job, AsyncJobContext context)
        {
            if (job == null)
                return;

            try
            {
                RunJob(job, context);
            }
            catch (Exception ex)
            {
                job.AddMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        /// <summary>ジョブのメッセージと結果を出力する(GHスレッドで実行する)</summary>
        private void Publish(TJob job, IGH_DataAccess DA)
        {
            if (job == null)
                return;

            foreach (KeyValuePair<GH_RuntimeMessageLevel, string> message in job.Messages)
                AddRuntimeMessage(message.Key, message.Value);

            PublishJob(job, DA);
        }

        /// <summary>
        /// 収集済みのジョブをバックグラウンドで計算する。
        /// 逐次実行では1本のタスク内で順に、並列実行では反復ごとに並列に計算する。
        /// </summary>
        private void StartBackgroundSolve()
        {
            List<TJob> jobs = new List<TJob>(_jobs);
            bool parallel = RunJobsInParallel;
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken token = cts.Token;
            long generation;

            lock (_stateLock)
            {
                _cts = cts;
                generation = Interlocked.Increment(ref _generation);
            }

            ProgressState progress = new ProgressState(jobs.Count, parallel);
            _progress = progress;

            GH_Document doc = OnPingDocument();
            Message = progress.Format();

            Task.Factory.StartNew(() =>
            {
                if (parallel)
                {
                    Parallel.For(0, jobs.Count, i => RunInBackground(jobs[i], i, token, progress));
                }
                else
                {
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        if (token.IsCancellationRequested)
                            break;
                        RunInBackground(jobs[i], i, token, progress);
                    }
                }

                lock (_stateLock)
                {
                    // キャンセル済み、または後続の solution が始まっていれば結果は捨てる。
                    // 新しい入力での計算がすでに走っているため、そちらが出力する
                    if (token.IsCancellationRequested || Interlocked.Read(ref _generation) != generation)
                        return;

                    _phase = AsyncPhase.Publishing;
                }

                StopProgress();

                // 直接 ExpireSolution(true) を呼ぶとバックグラウンドからでは再計算が
                // 走らないことがあるため、solution はドキュメント経由でスケジュールする
                if (doc != null)
                    doc.ScheduleSolution(1, d => ExpireSolution(false));
                else
                    Rhino.RhinoApp.InvokeOnUiThread((Action)(() => ExpireSolution(true)));
            }, CancellationToken.None, TaskCreationOptions, TaskScheduler.Default);
        }

        private void RunInBackground(TJob job, int index, CancellationToken token, ProgressState progress)
        {
            if (token.IsCancellationRequested)
                return;

            AsyncJobContext context = new AsyncJobContext(token,
                ratio => ReportProgress(progress, index, ratio));
            ExecuteJob(job, context);
        }

        /// <summary>
        /// 進捗の記録。表示自体はタイマで間引くため、ここでは値を差し替えるだけ。
        /// キャンセルは区切りごとにしか検知されず、その後も数区切りぶん通知が来るため、
        /// 実行中以外は捨てる(そうしないと "Cancelled" 表示が上書きされる)。
        /// </summary>
        private void ReportProgress(ProgressState progress, int index, double ratio)
        {
            if (_phase != AsyncPhase.Running || !ReferenceEquals(_progress, progress))
                return;
            if (double.IsNaN(ratio) || double.IsInfinity(ratio))
                return;

            progress.Report(index, ratio);

            if (!_progressTimer.Enabled)
                _progressTimer.Start();
        }

        /// <summary>進捗をキャンバスへ反映する。UI要素に触れるためGHスレッドへ移す</summary>
        private void DisplayProgress()
        {
            ProgressState progress = _progress;
            if (progress == null || _phase != AsyncPhase.Running)
                return;

            string text = progress.Format();
            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                // 判定から反映までの間にキャンセル・完了が挟まりうるので、
                // 実際に書き込む側でもう一度確かめる(表示を横取りしない)
                if (_phase != AsyncPhase.Running || !ReferenceEquals(_progress, progress))
                    return;
                Message = text;
                OnDisplayExpired(true);
            }));
        }

        private void StopProgress()
        {
            _progressTimer.Stop();
            _progress = null;
        }

        /// <summary>
        /// 走行中のタスクを無効化する。世代番号を進めることで、
        /// 既に計算を終えているタスクが結果を出力するのも防ぐ。
        /// CancellationTokenSource は IsCancellationRequested を読むだけの使い方で、
        /// WaitHandle も Register も CancelAfter も使わないため Dispose しない
        /// (走行中のタスクがトークンを参照している間に破棄する事故を避ける)。
        /// </summary>
        private void CancelRunningNoLock()
        {
            CancellationTokenSource cts = _cts;
            _cts = null;
            if (cts == null)
                return;

            Interlocked.Increment(ref _generation);
            cts.Cancel();
        }

        private void CancelRunning()
        {
            lock (_stateLock)
            {
                if (_phase == AsyncPhase.Running)
                    _phase = AsyncPhase.Idle;
                CancelRunningNoLock();
            }
            StopProgress();
        }

        /// <summary>
        /// 走行中の計算を打ち切る。下流のコンポーネントには前回の結果が残る。
        /// </summary>
        public void RequestCancellation()
        {
            if (!IsRunning)
                return;

            CancelRunning();
            Message = "Cancelled";
            OnDisplayExpired(true);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            CancelRunning();
            base.RemovedFromDocument(document);
        }

        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close || context == GH_DocumentContext.Unloaded)
                CancelRunning();
            base.DocumentContextChanged(document, context);
        }

        /// <summary>
        /// 非同期実行の切り替えとキャンセルをメニューに加える。
        /// 派生クラスの項目をこれより上に置く場合は、項目を追加してから base を呼ぶ。
        /// </summary>
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);

            Menu_AppendSeparator(menu);

            Menu_AppendItem(menu, "Asynchronous Solve", (s, e) =>
            {
                RecordUndoEvent("Asynchronous solve");
                CancelRunning();
                _asyncEnabled = !_asyncEnabled;
                ExpireSolution(true);
            }, true, _asyncEnabled);

            Menu_AppendItem(menu, "Cancel", (s, e) => RequestCancellation(), IsRunning);
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetBoolean("AsyncSolve", _asyncEnabled);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            bool asyncEnabled = true;
            if (reader.TryGetBoolean("AsyncSolve", ref asyncEnabled))
                _asyncEnabled = asyncEnabled;
            return base.Read(reader);
        }
    }
}
