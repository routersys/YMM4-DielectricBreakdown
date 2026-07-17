# v1.0.0 - 誘電破壊 for YMM4

YukkuriMovieMaker4向けの誘電破壊エフェクトプラグインの初回リリースです。
素材の不透明な部分を電極とみなし、誘電絶縁破壊モデルで放電経路を成長格子の上に成長させ、稲妻として描画します。
放電経路はシードから決定論的に決まり、成長のパラメータで経路が伸びて対象へ到達するまでの遷移を表現できます。
計算はComputeSharpの計算シェーダーがDirect3D 12で実行し、YMM4のDirect3D 11側とは共有テクスチャおよび共有フェンスで接続します。
8言語のリソース構成のUIを備えます。

---

## 新機能

### 1. 放電成長の計算パイプライン

`DielectricBreakdownPipeline`は、シルエット、成長、描画の3段階の計算シェーダーを`ComputeContext`へ記録して実行します。成長格子のバッファーは計算領域の大きさと品質に応じて確保し、サイズが変わらないフレームでは再利用します。処理の流れは次のとおりです。

1. `SilhouetteShader`が、各格子セルに対応する画素のアルファ値を調べ、しきい値`0.05`を超えるセルを素材の輪郭として記録します。
2. `ElectrodeInitShader`が、輪郭の境界セルを電極として置き、内部のセルを成長の対象から除外します。
3. `ChargeRowCountShader`・`ChargeRowOffsetShader`・`ChargeFillShader`が、電極セルの一覧を作り、`InitialPotentialShader`が全セルの初期ポテンシャルを電極からの距離に基づいて計算します。
4. `GrowthShader`が、成長の全ステップを単一のスレッドグループで実行します。
5. `MainChannelShader`が、到達点から親をたどって主経路へ印を付け、`IntensityShader`が各セルの発光強度を主経路からの距離に応じて決めます。
6. `GlowDepositShader`が、経路の線分に沿って光を1/4解像度の格子へ堆積し、`GlowBlurHorizontalShader`と`GlowBlurVerticalShader`がぼかします。
7. `JumpFloodSeedShader`と`JumpFloodPassShader`が、格子解像度のジャンプフラッドで各セルの最近傍の構造セルを求めます。
8. `RenderShader`が、画素ごとに最近傍の構造セルの線分までの距離から芯を描き、発光を合成して出力します。

| シェーダー | 役割 |
|---|---|
| `SilhouetteShader` | 画素のアルファ値から素材の輪郭を求める |
| `ElectrodeInitShader` | 輪郭の境界セルを電極として置く |
| `ChargeRowCountShader` / `ChargeRowOffsetShader` / `ChargeFillShader` | 電極セルの一覧を作る |
| `InitialPotentialShader` | 初期ポテンシャルを計算する |
| `GrowthShader` | 放電経路を成長させる |
| `MainChannelShader` | 主経路へ印を付ける |
| `IntensityShader` | セルごとの発光強度を決める |
| `GlowDepositShader` | 経路に沿って光を堆積する |
| `GlowBlurHorizontalShader` / `GlowBlurVerticalShader` | 堆積した光をぼかす |
| `JumpFloodSeedShader` / `JumpFloodPassShader` | 最近傍の構造セルを求める |
| `RenderShader` | 芯と発光を描画する |

### 2. 単一スレッドグループの協調成長カーネル

`GrowthShader`は、`1024`スレッドの単一グループが成長ループ全体を1回のディスパッチで実行します。ステップごとのディスパッチとバリアを排除し、ステップ間の同期はグループ内のバリアで行います。

- 成長の候補は、構造に隣接する空きセルだけを候補リストへ保持し、毎ステップの走査を候補に限定します。
- ポテンシャルは、寄与項を倍率`16384`の固定小数点で個別に量子化して整数で加算します。加算の順序に依存しないため、新しい候補への履歴の合算を全スレッドで並列に行えます。
- スコアの最小値・最大値・最良値・選択セルの還元は、`groupshared`メモリ上の原子操作で行います。
- 成長先の選択は、ポテンシャルとGumbelノイズによるスコアの最大値と、同点時のセル番号の最小値で決まるため、決定論的です。
- 経路が対象へ到達した後のステップは、ループを打ち切って実行しません。

各ステップで選んだセルと親セルは`growthLog`と`parentLog`へ記録し、後段の可視範囲の計算に使用します。

### 3. 可視範囲への出力矩形の最小化

成長の完了後、`growthLog`・`parentLog`と到達情報をCPUへ読み戻します。読み戻す量は成長の記録だけで、画素は読み戻しません。成長のパラメータが定める可視セルの集合から正確なバウンディングボックスを求め、太さと発光の広がりの分の余白を加えた矩形だけを描画します。

- 矩形は発光格子に合わせて4画素境界へそろえます。
- 出力テクスチャは矩形の大きさで確保し、Direct2Dの`Crop`と`AffineTransform2D`で元の位置へ合成します。
- 矩形の外側は描画も合成も行わないため、距離を大きくしても処理量は稲妻が実在する範囲に比例します。

### 4. 構造キャッシュ

放電経路を決める入力が変わらないフレームでは、成長の計算を再利用します。

- 素材の輪郭は、シルエットの計算後にセル位置のハッシュの総和とXORの2値へ集約し、8個の整数の読み戻しで前フレームと比較します。
- 輪郭のハッシュ、計算領域の大きさ、品質、シード、方向、距離、分岐が一致する場合は、成長段階と記録の読み戻しを実行しません。
- 構造が同じで、成長、太さ、発光、色、出力矩形も変わらないフレームでは、描画段階も実行せず、前フレームの出力テクスチャを使用します。

### 5. Direct3D 11・Direct3D 12相互運用

`DielectricBreakdownGpuInterop`は、YMM4のDirect3D 11・Direct2D側と、ComputeSharpのDirect3D 12側を接続します。ComputeSharpの`GraphicsDevice`は、YMM4が使うDXGIアダプターのLUIDと一致するものを選びます。

入力と出力は、ComputeSharpで確保した共有テクスチャをDirect3D 11のテクスチャとして開き、Direct2Dのビットマップとして扱います。入力のテクスチャは素材の大きさで確保し、出力のテクスチャは可視範囲の矩形を収める容量で確保して拡大時だけ作り直します。両デバイスの同期は、Direct3D 12のフェンスを共有フェンスとしてDirect3D 11側で開いて行います。

`BeginCompute`は、Direct3D 11のコマンドを送出したうえでDirect3D 12側を待機させ、`EndCompute`は、Direct3D 12側の完了をDirect3D 11側で待ちます。

Direct3D 12デバイスの取得や共有リソースの作成に失敗した場合は、`TryCreate`が`null`を返し、エフェクトを適用せず入力映像を表示します。

### 6. カスタムシェーダーによる合成

`DielectricBreakdownCustomEffect`は、`[CustomEffect(2)]`の2入力エフェクトです。入力0は元映像、入力1は描画した稲妻です。ピクセルシェーダー`DielectricBreakdown.hlsl`の`main`は、`amount`が0以下のとき元映像をそのまま返し、そうでないときは稲妻のRGBをアルファでクランプし、`source + lightning - source * lightning`のスクリーン合成で加算します。

定数バッファーは`Amount`と3つの詰め物で16バイトです。`MapInputRectsToOutputRect`は2つの入力矩形の和集合を出力矩形とします。稲妻は素材の外側へ広がるため、出力範囲は素材より大きくなります。

シェーダーリソース: `pack://application:,,,/DielectricBreakdown;component/Shaders/DielectricBreakdown.cso`（ps_5_0、`ShaderResourceUri.Get`が生成）

### 7. エフェクト定義とパラメータ

`DielectricBreakdownEffect`は、YMM4の映像エフェクトとして宣言されます。

`[VideoEffect]`属性は以下のパラメーターで宣言されます。

- 表示名: `Texts.DielectricBreakdown`（ローカライズキー、日本語では「誘電破壊」）
- カテゴリー: `VideoEffectCategories.Decoration`・`VideoEffectCategories.Animation`
- 検索タグ: `TagLightning`・`TagElectric`・`TagDischarge`
- `IsAviUtlSupported = false`によりAviUtl向けEXO出力は非対応
- `ResourceType = typeof(Texts)`でローカライズリソースを指定

公開プロパティは以下のとおりです。基本項目は「基本」グループ、放電項目は「放電」グループ、描画項目は「描画」グループに属します。

| プロパティ | 型 | デフォルト | 内部範囲 | アニメーション |
|---|---|---|---|---|
| `Amount` | `Animation` | 100 | 0〜100 | あり |
| `Growth` | `Animation` | 100 | 0〜100 | あり |
| `Quality` | `DielectricBreakdownQuality` | `High` | — | なし |
| `Angle` | `Animation` | 0 | -36000〜36000 | あり |
| `Reach` | `Animation` | 75 | 1〜400 | あり |
| `Branching` | `Animation` | 50 | 0〜100 | あり |
| `Seed` | `int` | 0 | 0〜int.MaxValue | なし |
| `Thickness` | `Animation` | 2.5 | 0.1〜100 | あり |
| `Glow` | `Animation` | 50 | 0〜100 | あり |
| `LightningColor` | `Color` | #FFB4C8FF | — | なし |

`GetAnimatables`は`Amount`・`Growth`・`Angle`・`Reach`・`Branching`・`Thickness`・`Glow`を返します。`Seed`は負値を代入すると0へ丸めます。

`CreateExoVideoFilters`は空のシーケンスを返します（EXO非対応）。`CreateVideoEffect`は映像処理用のインスタンスを生成します。エフェクトを最初に生成したときに、更新確認を一度だけ開始します。

### 8. フレームごとの更新

各フレームでYMM4の`EffectDescription`からフレーム位置、アイテム長、FPSを取得し、アニメーション値を評価します。値をパイプラインが前提とする範囲へ制限してから転送します。

| パラメータ | 変換 |
|---|---|
| `Amount` | `value / 100` をカスタムシェーダーの`Amount`へ |
| `Growth` | `value / 100` を0〜1へクランプ |
| `Angle` | 度数をそのまま |
| `Reach` | `value / 100` を0.01〜4へクランプし、素材の長辺に掛けて画素数へ |
| `Branching` | `value / 100` を0〜1へクランプ |
| `Thickness` | 0.05〜100pxへクランプ |
| `Glow` | `value / 100` を0〜1へクランプ |
| `LightningColor` | RGB各成分を0〜1へ |
| `Seed` | 0以上へクランプ |

強さが0以下のとき、または成長が0以下のときは、稲妻を描画せず入力映像をそのまま出力します。入力の範囲が有限でない場合や、計算領域の余白を確保できない場合も、入力映像を表示します。

### 9. 品質設定

品質は、成長格子の解像度と成長ステップ数の上限をまとめて切り替えます。

| 品質 | 格子解像度 | 成長ステップ上限 |
|---|---:|---:|
| 標準 | 144 | 640回 |
| 高品質 | 192 | 1024回 |
| 最高品質 | 256 | 1536回 |

格子解像度は計算領域の長辺のセル数です。短辺のセル数は計算領域の縦横比に合わせ、最小4セルとします。実際のステップ数は距離に応じたセル数から決まり、上限で打ち切ります。

### 10. ローカライズ

`Texts`クラスは`[AutoGenLocalizer]`属性を持つ`partial`クラスとして宣言されます。
`YukkuriMovieMaker.Generator`のソースジェネレーターが`Texts.csv`を処理し、各ロケールのリソースファイルを自動生成します。

対応リソース: 日本語（`ja-jp`）・英語（`en-us`）・中国語簡体字（`zh-cn`）・中国語繁体字（`zh-tw`）・韓国語（`ko-kr`）・スペイン語（`es-es`）・アラビア語（`ar-sa`）・インドネシア語（`id-id`）

主なローカライズキーは以下のとおりです。

| キー | ja-jp |
|---|---|
| `DielectricBreakdown` | 誘電破壊 |
| `BasicGroup` | 基本 |
| `DischargeGroup` | 放電 |
| `AppearanceGroup` | 描画 |
| `Amount` | 強さ |
| `Growth` | 成長 |
| `Quality` | 品質 |
| `Angle` | 方向 |
| `Reach` | 距離 |
| `Branching` | 分岐 |
| `Seed` | シード |
| `Thickness` | 太さ |
| `Glow` | 発光 |
| `LightningColor` | 色 |
| `QualityBalanced` | 標準 |
| `QualityHigh` | 高品質 |
| `QualityUltra` | 最高品質 |
| `TagLightning` | 稲妻 |
| `TagElectric` | 電撃 |
| `TagDischarge` | 放電 |
| `UpdateAvailableMessage` | 新しいバージョン {0} が公開されています。 |
