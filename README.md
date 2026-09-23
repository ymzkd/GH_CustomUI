# GH_CustomUI

Grasshopper (Rhino 8) のコンポーネント・パラメーターを作るための共通ライブラリです。

| 機能 | できること | 主なクラス |
|---|---|---|
| UIパーツ | コンポーネント本体の下に、ボタン・ドロップダウン・グラフなどを並べる | `GH_UIComponentAttributes`, `Parts/` |
| インラインパラメーター | 入力パラメーターの横に、単位や属性を選ぶ小さなUIを置く | `GH_InlineParam<T>`, `InlineParams/` |
| 非同期コンポーネント | 重い計算をバックグラウンドで行い、キャンバスの操作を止めない | `GH_AsyncComponent<TJob>`, `Async/` |

## 使い方の流れ

- **UIパーツ**: Attributes を `GH_UIComponentAttributes` から派生させ、`AddUI` でパーツを並べる。パーツの状態は .gh に保存される
- **インラインパラメーター**: `EnumOptionParam` などを入力に追加し、`SolveInstance` で `Option` を読む。表示には `GH_UIComponentAttributes` が必要
- **非同期コンポーネント**: `GH_AsyncComponent<TJob>` を継承し、`CollectJob` (入力の読み取り)・`RunJob` (計算)・`PublishJob` (出力) の3つを実装する。同期実行への切り替え、キャンセル、進捗表示は基底が提供する

## 動作環境

- .NET 8 (`net8.0-windows`)、x64
- Grasshopper 8.19.25132.1001、MessagePack 3.1.8

```
dotnet build GH_CustomUI.csproj -c Debug -p:Platform=x64
```
