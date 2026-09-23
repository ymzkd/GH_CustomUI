# AGENTS.md

GH_CustomUI を利用・変更するときの詳しい情報です。概要は [README.md](README.md) を参照してください。

## ビルド

```
dotnet build GH_CustomUI.csproj -c Debug -p:Platform=x64
```

- .NET 8 (`net8.0-windows`)、x64 のみ
- Grasshopper 8.19.25132.1001 (NuGet)、MessagePack 3.1.8
- 利用側のプロジェクトからは `ProjectReference` で参照する

## コードの規約

- 名前空間はフォルダに関係なくすべて `GH_CustomUI`
- C# のソースは UTF-8 (BOM 付き)、改行は CRLF
- `ImplicitUsings` / `Nullable` は無効。`using` は明示する
- コメントは日本語。キャンバスに表示される文字列と右クリックメニューの項目は英語
- 汎用ライブラリなので、特定の利用側プロジェクトの都合をここに持ち込まない

### 互換性を壊さないために

.gh ファイルに保存される値は、後から変えると既存の定義が読めなくなる。

- UIパーツの保存名は「型名 + `Index`」。`AddUI` の順番を変えると `Index` がずれる
- インラインパラメーターの GUID は型名から決まる (`CreateDeterministicGuid`)。クラス名・名前空間を変えると GUID が変わる
- `GH_AsyncComponent` は `AsyncSolve` キーを使う
- 保存キーの名前を変える場合は、古いキーも読めるようにしておく

## UIパーツ

コンポーネントの Attributes を `GH_UIComponentAttributes` から派生させ、コンストラクタで `AddUI` してパーツを並べる。パーツは登録順に上から積まれ、コンポーネントの幅は最も広いパーツに合わせて広がる。

```csharp
public class MyAttributes : GH_UIComponentAttributes
{
    public DropDownUI<MyMode> Mode;

    public MyAttributes(IGH_Component owner) : base(owner)
    {
        Mode = new DropDownUI<MyMode>((MyMode[])Enum.GetValues(typeof(MyMode)), 0);
        Mode.ValueChanged = () => Owner.ExpireSolution(true);

        AddUI(new SeparatorLabelUI("Mode"));
        AddUI(Mode);
    }
}

public class MyComponent : GH_Component
{
    public override void CreateAttributes() => m_attributes = new MyAttributes(this);

    protected override void SolveInstance(IGH_DataAccess DA)
    {
        MyMode mode = ((MyAttributes)Attributes).Mode.SelectedItem;
        // ...
    }
}
```

### 状態の保存

- パーツの状態は Attributes の `Write` / `Read` で .gh ファイルに保存される
- 保存名はパーツの型名と登録順 (`Index`) から作られる。並びを変える場合は、`AddUI` の前に `Index` を明示して以前の番号を保つ
- `AddUI` を経由せずに手動で配置したパーツは `Index` が -1 のままになり、同じ型が複数あると保存名が衝突する
- 値が変わったときのドキュメントへの変更通知(未保存マーク)はパーツ側で行う (`NotifyDocumentModified`)。再計算が必要な場合だけ、`ValueChanged` などで `ExpireSolution(true)` を呼ぶ

### パーツ一覧 (`Parts/`)

| クラス | 用途 |
|---|---|
| `ButtonUI` | 押しボタン。ラベルまたはアイコン。`OnClicked` |
| `ToggleButtonUI` | オン/オフを保持するボタン |
| `ButtonTogglesUI` | ボタンを並べた選択UI。単一選択/複数選択を切り替え可能 |
| `GroupTogglesUI` | ラベル付きのトグルを並べた選択UI。単一選択/複数選択を切り替え可能。`Toggles` |
| `CheckboxUI` | 単一のチェックボックス |
| `SwitchButtonUI` | スイッチ。`Value`, `ValueChanged` |
| `DropDownUI<T>` | ドロップダウン。`SelectedItem`, `ValueChanged` |
| `SliderUI` | スライダー (実装途中) |
| `TextBoxUI` | テキスト入力。`Contents`, `ValueChanged` |
| `LabelUI` | テキスト表示 |
| `SeparatorLabelUI` | 見出し付きの区切り線 |
| `ContainerUI` | 子パーツを縦 (既定) または横に並べる入れ物。`Orientation` |
| `ExpanderUI` | 折りたためる入れ物 |
| `GraphUI` | 折れ線グラフ。シークバー付き |

### 独自のパーツ

`GH_UIParts` を継承し、`Height` / `MinWidth` / `Render` と必要なマウスイベント、状態があれば `Write` / `Read` を実装する。マウスイベントで `Capture` を返すと、以降の `MouseMove` / `MouseUp` はそのパーツへ直接送られる (ドラッグ操作用)。

パラメーター本体にUIを描く場合(サイズ変更できるパネル型のパラメーターなど)は、`GH_UIResizableParamAttributes<T>` を使う。

## インラインパラメーター

入力値とは別に、「入力値に付随する指定」(単位、属性など)をパラメーターの横のUIで選ばせる仕組み。設定はパラメーター自身の `Write` / `Read` で保存されるため、.gh の保存やコピー&ペースト、Extract parameter で入力値と一緒に運ばれる。Undo にも対応している。

| クラス | UI |
|---|---|
| `EnumOptionParam<TGoo, TEnum>` | enum のドロップダウン。enum の名前で保存するので、後から並びを変えてもよい |
| `BoolOptionParam<TGoo>` | トグル |
| `NumberOptionParam<TGoo>` | 数値入力。最小値・最大値・表示書式を指定できる |

```csharp
protected override void RegisterInputParams(GH_InputParamManager pManager)
{
    pManager.AddParameter(new EnumOptionParam<GH_Number, LengthUnit>(
        "Length", "L", "Length value", LengthUnit.mm));
}

protected override void SolveInstance(IGH_DataAccess DA)
{
    double length = 0.0;
    DA.GetData(0, ref length);
    LengthUnit unit = ((EnumOptionParam<GH_Number, LengthUnit>)Params.Input[0]).Option;
    // ...
}
```

- **インラインUIが描かれるのは、コンポーネントの Attributes が `GH_UIComponentAttributes` (またはその派生) のときだけ。** 標準の Attributes のままでは表示されない
- UIが扱うのは入力値ではなく設定なので、ワイヤーが接続されていても操作できる (`InlineUIEnabled` で変更可)
- 設定は既定ではプロパティとして公開されるだけで、使うかどうかはコンポーネントの `SolveInstance` が判断する。単位換算のように下流へ正規化した値を流したい場合は、`AppliesInlineState` と `TryApplyInlineState` を override する

### 独自のインラインパラメーター

`GH_InlineParam<T>` を継承して `InlineUI` と `WriteInlineState` / `ReadInlineState` を実装する。

- 設定の変更は `SetInlineState` を通す。Undo 登録、ドキュメントへの変更通知、再計算がまとめて行われる
- `ReadInlineState` は、値が保存されていなくても失敗させず既定値のままにする (古いファイルを読めるように)
- ジェネリックなパラメーターは閉じた型ごとに GUID が必要になるので、`CreateDeterministicGuid` で型名から作る
- 既存のパラメータークラスにUIを足したい場合は、`IInlineParamUI` を直接実装してもよい

## 非同期コンポーネント

`GH_AsyncComponent<TJob>` を継承すると、計算をバックグラウンドで行うコンポーネントになる。1回の solution を次の3段階に分けて実装する。

| メソッド | 実行スレッド | 役割 |
|---|---|---|
| `CollectJob` | GH (反復ごと) | 入力を読み取り、ジョブを組み立てる |
| `RunJob` | バックグラウンド | 計算する |
| `PublishJob` | GH (反復ごと) | 結果を出力する |

全反復の読み取りが終わってから計算を始め、終わると solution をやり直して結果を出力する(出力パス)。計算中に入力が変わると、走行中の計算は打ち切られてやり直しになる。

### 実装例

```csharp
public sealed class SumJob : AsyncJob
{
    public List<double> Values = new List<double>();
    public double Result;
}

public class SlowSumComponent : GH_AsyncComponent<SumJob>
{
    public SlowSumComponent()
        : base("SlowSum", "SlowSum", "Sums values in the background", "Example", "Async")
    {
    }

    protected override void RegisterInputParams(GH_InputParamManager pManager)
        => pManager.AddNumberParameter("Values", "V", "Values", GH_ParamAccess.list);

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        => pManager.AddNumberParameter("Sum", "S", "Sum", GH_ParamAccess.item);

    // GHスレッド: 入力と、計算に必要なUIの状態はここで読み取っておく
    protected override SumJob CollectJob(IGH_DataAccess DA)
    {
        SumJob job = new SumJob();
        if (!DA.GetDataList(0, job.Values))
            job.AddMessage(GH_RuntimeMessageLevel.Warning, "No values.");
        return job;
    }

    // バックグラウンド: ジョブが持つデータだけを使う
    protected override void RunJob(SumJob job, AsyncJobContext context)
    {
        for (int i = 0; i < job.Values.Count; i++)
        {
            if (context.IsCancellationRequested)
                return;
            job.Result += job.Values[i];
            context.ReportProgress((i + 1) / (double)job.Values.Count);
        }
    }

    // GHスレッド: ジョブに溜めたメッセージはこの前に基底が出力する
    protected override void PublishJob(SumJob job, IGH_DataAccess DA)
        => DA.SetData(0, job.Result);

    public override Guid ComponentGuid => new Guid("00000000-0000-0000-0000-000000000000");
}
```

### 守ること

- **`RunJob` では GH のオブジェクトに触らない。** 入力、Attributes、UIパーツの値、ドキュメントなどは `CollectJob` で読み取ってジョブに入れておく
- **メッセージは `job.AddMessage` で出す。** 出力パスで solution がやり直されるため、計算中に `AddRuntimeMessage` を呼んでも消える
- **入力に不備があっても `CollectJob` は null を返さない。** メッセージだけを持たせたジョブを返し、`RunJob` / `PublishJob` 側で結果がないことを判定する
- **キャンセルは区切りごとに `context.IsCancellationRequested` で確かめる。** 打ち切られた計算や古くなった計算の結果は基底が捨てる。`context.Token` に例外で応える API を使うと、デバッグ中に第一機会例外でブレークしやすい点に注意
- `SolveInstance` は基底が実装しているため override できない。`BeforeSolveInstance` / `AfterSolveInstance` を override する場合は必ず base を呼ぶ
- `RunJob` が投げた例外は、エラーメッセージとして出力される

### 基底が提供するもの

- **同期実行への切り替え**: 右クリックメニューの `Asynchronous Solve`。同期実行では3段階をそのまま続けて行う。Galapagos などの最適化ソルバーや Rhino.Compute のように、同じ solution 内で結果が出ることを前提とする呼び出し元では同期実行にする。設定は `AsyncSolve` キーで .gh に保存される (派生クラスでこのキーを使わない)。既定値を変える場合は、コンストラクタで `AsyncEnabled = false` とする
- **キャンセル**: 右クリックメニューの `Cancel`、またはコードから `RequestCancellation()`。下流のコンポーネントには前回の結果が残る
- **進捗表示**: `context.ReportProgress(0.0-1.0)` の値がコンポーネントの `Message` に表示される。表示は約 300 ms ごとに間引かれるので、細かく呼んでもよい。逐次実行で複数反復あるときは「何番目/全体 進捗率」、並列実行では平均を表示する
- **下流の保持**: 計算中は下流を無効化せず、前回の結果を残したままにする。出力パスではじめて下流へ伝播させる (計算中に結果が一瞬消えるのを防ぐ)
- **ドキュメントを閉じたとき・コンポーネントを削除したとき**の計算の打ち切り。ドキュメントのタブを切り替えただけでは打ち切らず、計算は裏で続く

右クリックメニューには、区切り線に続けて上記2項目が追加される。独自の項目をその上に置く場合は、項目を追加してから base を呼ぶ。

```csharp
public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
{
    Menu_AppendItem(menu, "Settings...", (s, e) => ShowSettings());
    base.AppendAdditionalMenuItems(menu);
}
```

### 実行方法の調整

いずれもコンストラクタで設定する。

- `RunJobsInParallel = true`: 複数反復のジョブを並列に計算する (既定は逐次)。ジョブごとに大きなメモリを抱える計算(行列の因数分解など)では逐次のままにする
- `TaskCreationOptions = TaskCreationOptions.LongRunning`: 長時間の計算がスレッドプールを占有しないようにする

### 内部の仕組み (変更するとき向け)

- 状態は `Idle → Collecting → Running → Publishing → Idle` と遷移する。`Publishing` の間の solution が出力パス
- 走行中の計算を打ち切るときは `CancellationTokenSource` をキャンセルし、世代番号を進める。計算を終えたタスクは世代番号が変わっていれば結果を捨てる (キャンセルの検知が遅れた場合も古い結果を出さない)
- 出力パスは `GH_Document.ScheduleSolution` 経由で起動する。バックグラウンドから `ExpireSolution(true)` を直接呼ぶと再計算が走らないことがあるため
- `Publishing` への切り替えはバックグラウンドではなく、スケジュールした solution の直前に GHスレッドで行う。その時点で世代番号が変わっているか、コンポーネントが入力の変化で無効化済みなら出力しない (新しい入力に古い結果を出さないため)
- `CollectJob` が例外を投げた反復は、メッセージだけを持つ埋め草のジョブで埋める。反復インデックスとジョブの並びの対応を保つため
- `CancellationTokenSource` はトークンのフラグを読むだけの使い方なので Dispose しない (走行中のタスクが参照している間に破棄する事故を避ける)
- 進捗は計算ごとに記録先を作り直す。打ち切られた古い計算からの通知が、新しい計算の表示に混ざらないようにするため
