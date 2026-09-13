<img src="Docs/assets/icon.png" width="96" align="right" alt="KitchenXR">

# KitchenXR

Meta Quest 3 の MR（パススルー）で動く料理アシスタントです。レシピ・材料・タイマー・
「ながら見」の動画を**キッチンの空間に固定**し、濡れた手でも触れる大きなボタンだけで工程を進めます。
レシピ帳と動画リストは別リポジトリのサーバ **AI Manor**（manor）が持ち、KitchenXR はその API を
**読む側**に徹します（依存は KitchenXR → manor の一方向）。
取得したレシピ・画像・一覧は端末に写すので、**通信が切れても調理は止まりません**。

プロトタイプ（v0 完了、v1.0.12 まで実機で確認済み）です。

## 動作環境

| | |
|---|---|
| 端末 | Meta Quest 3（パススルー・ハンドトラッキング。コントローラでも操作できる） |
| Unity | **6000.3.24f1**（Mixed Reality テンプレートから作成） |
| XR | OpenXR 1.17.1 ／ XR Interaction Toolkit **3.5.1** ／ XR Hands 1.8.1 |
| MR | AR Foundation **6.5.0** ／ Unity OpenXR: Meta 2.5.1（パススルー・永続アンカー） |
| UI | UI Toolkit（ワールド空間パネル）＋ Noto Sans JP の TextCore Font Asset |
| 非同期・JSON | UniTask ／ Newtonsoft.Json 3.2.2 |
| 動画 | `Assets/TLab/` の WebView（TLabWebView 系）を包んだ YouTube プレイヤー |

Android のビルド設定は `KitchenXR.App.Editor.AndroidPlayerSetup` が揃えます
（Graphics API は Vulkan＋OpenGLES3 の併記・最小 API 26 以上・Internet permission・
LAN の HTTP を許可）。OpenXR の「Force Remove Internet Permission」が立っていると
Internet permission が剥がされるため、そこは検算だけ行います。

## 開き方とビルド

Unity Editor で `Assets/KitchenXR/Scenes/Kitchen.unity` を開きます。シーンは手で組まず
**生成**します（Editor メニューではなくバッチから）。

```powershell
# シーンを組み直す（MR テンプレートの SampleScene を元に Kitchen.unity を作る）
# (`unity run` は引数を Editor に渡さないので Unity.exe を直接呼ぶ)
& "C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor\Unity.exe" -batchmode -nographics -quit -projectPath . -executeMethod KitchenXR.App.Editor.KitchenSceneBuilder.Build -logFile Build/scene-build.log

# 試験
unity test --mode EditMode      # 189 件
unity test --mode PlayMode      #  83 件

# Android（Quest 3）の APK。出力は Build/KitchenXR_v<bundleVersion>.apk
unity build --target Android --execute-method KitchenXR.App.Editor.AndroidBuilder.PerformBuild
```

CLI／バッチで Editor を起動すると終了時に `UserSettings/Layouts/*.dwlt` が書き出されて
エディタのレイアウトが初期に戻ります。前後で退避してください。

```powershell
Tools/editor-layout-backup.ps1 -Backup    # 起動前
Tools/editor-layout-backup.ps1 -Restore   # 終了後（-Status で中身を確認）
```

Editor で Play すると AR Foundation の **XR Simulation** が模擬環境のシーンを additive で
足します（正常）。実機の映像を見ながら Play したいときは Project Settings >
XR Plug-in Management の **Standalone** タブの Plug-in Provider を OpenXR に切り替えます
（Android タブは実機ビルド用なので触りません）。Play 中に Main Camera の回転が変わるのは
`TrackedPoseDriver` が毎フレーム姿勢を書き込むためで、Play を抜ければ元に戻ります。

## manor との繋ぎ方（要点）

板に文字入力を置かない設計なので、**番号は端末に出し、Web で許可します**。

1. PC で manor を LAN に向けて立てる（`--host 0.0.0.0` または `[web] host`）。
2. Quest で KitchenXR を起動すると、一覧の板に **6 桁の番号**が出る。
3. manor の Web の 設定 → 端末 でその番号を入れ、利用者を選んで「許可」。
4. 番号が消えて、レシピ帳と動画リストが並ぶ。鍵は端末内に控えるので次回からは出ない。

manor が見つからなければ同梱の見本レシピだけでアプリは動きます。
探索・`manor.json`・端末内のファイル・ログの見方は [`Docs/MANOR.md`](Docs/MANOR.md) に。

## フォルダ構成

```
Assets/KitchenXR/
  Domain/        レシピ・工程・進行の状態機械・タイマー（UnityEngine 非依存）
  Platform/      アンカー／パススルー／手の入力の口と、ArFoundation・Null の実装
  Net/           manor の口（ManorClient・探索・ペアリング）と端末内の保管庫
  Presentation/  板（UI Toolkit）・ポーク・配置モード・手首メニュー・指先カーソル
    Video/       WebView を包む動画の板（WebView に触るのはここだけ）
    Hazard/      注意の板（火気・刃物…）とコンロの領域・近づいたときの注意
  App/           Bootstrap（どのアダプタを挿すか）／Editor（シーン生成・ビルド）
  Tests/         EditMode・PlayMode
Assets/TLab/     WebView と YouTube プレイヤー（第三者資産）
Docs/            設計・構成・manor との連携・ロードマップ
Tools/           エディタのレイアウト退避
```

- 設計と決定の記録: [`Docs/DESIGN.md`](Docs/DESIGN.md)
- 層・板・入力・オフライン・アンカー: [`Docs/ARCHITECTURE.md`](Docs/ARCHITECTURE.md)
- manor との連携と API・契約 JSON: [`Docs/MANOR.md`](Docs/MANOR.md)
- 段取りと残課題: [`Docs/ROADMAP.md`](Docs/ROADMAP.md)
- レシピの契約 JSON の実例: [`Docs/samples/chahan.recipe.json`](Docs/samples/chahan.recipe.json)

## ライセンスの注記

このリポジトリには第三者の資産が同梱されています。**それぞれの元のライセンスに従います。**

| 資産 | 置き場 | ライセンス |
|---|---|---|
| TLabWebView（Android の WebView） | `Assets/TLab/TLabWebView/` | `Assets/TLab/TLabWebView/LICENSE.md` |
| TLabVKeyborad | `Assets/TLab/TLabVKeyborad/` | `Assets/TLab/TLabVKeyborad/LICENSE.md` |
| TLabWebViewVR ／ YouTube プレイヤー | `Assets/TLab/TLabWebViewVR/`・`Assets/TLab/Youtube/` | 上記 TLabWebView に準ずる |
| Noto Sans JP（Font Asset の元フォント） | `Assets/KitchenXR/Presentation/UI/Fonts/` | SIL Open Font License 1.1 |
| XR Interaction Toolkit ／ XR Hands のサンプル | `Assets/Samples/` | Unity Companion License |

`.gitignore`・`.gitattributes` は Unity 公式テンプレート（CC0-1.0 ／ MIT）から取っています。
プロジェクト本体（`Assets/KitchenXR/`・`Docs/`・`Tools/`）のライセンスは未定です。

## 開発の進め方

`main` は動く版だけ。機能は `feature/<名前>`、調査は `research/<名前>` のブランチで作業し、
実機で「済みの印」を確かめてから `main` へ取り込みます。段取りは [`Docs/ROADMAP.md`](Docs/ROADMAP.md)。
