# 調査: Quest 3 のカメラ取得と画像認識（v2 認識の前段）

**種類**: 技術調査（実装・パッケージ追加は行っていない）／**確認日**: 2026-09-12／
**対象**: [`ROADMAP.md`](../ROADMAP.md) の **v2 認識**と、その前段の「空間の注意表示」。
**前提**: Unity 6000.3.24f1・OpenXR 1.17.1・**Unity OpenXR: Meta 2.5.1**・AR Foundation 6.5.0・
XRI 3.5.1・XR Hands 1.8.1・**Unity Inference Engine（`com.unity.ai.inference`）2.6.1**
（[`Packages/manifest.json`](../../Packages/manifest.json)。Meta XR Core SDK・MRUK は**未導入**）。
出典は §6 に番号で、本文から `[n]` で参照します。

## 0. 要旨

| 問い | 結論 |
|---|---|
| 今の構成（Meta XR Core SDK 無し）でカメラ画像は取れるか | **取れる。** Unity OpenXR: Meta 2.4.0-pre.1 以降に CPU 画像取得が入り、2.5.1 は対象内 `[5]`。**追加パッケージは不要**で、設定・権限・`minSdk 32` の3点だけ |
| `ARCameraManager.TryAcquireLatestCpuImage` で取れるか | **取れない。** AR Foundation の汎用表では Meta の "Camera image" は非対応 `[8]`。provider 固有の **`MetaOpenXRCameraSubsystem`** を直接叩く `[6]` |
| Meta の公式サンプルは流用できるか | **コードは流用不可**（MRUK ＋ Meta XR Core SDK 前提 `[4]`）。読み物としてのみ有効 |
| 認識は端末内から始めるか PC 側からか | **PC（manor）側から。** 台所の対象（鍋・フライパン・火・湯気）は COCO に 1 つも無い |
| ②はカメラ無しで何が出来るか | **注意板の常設と接近警告まで全部。** 領域は Scene のラベルでは取れず、**手で囲って保存**する |

## 1. カメラ画像の取得

### 1.1 三つの道

| 道 | 何を使う | 追加パッケージ | 内部パラメータ／姿勢 | 判定 |
|---|---|:--:|---|:--:|
| **A. Unity OpenXR: Meta** | `MetaOpenXRCameraSubsystem`（AR Foundation の Camera subsystem の provider） | **無し** | Display／Projection 行列は可 `[8]`。`XRCameraIntrinsics` は表に載らない | **採る** |
| B. Meta Passthrough Camera API | MRUK の `PassthroughCameraAccess` ＋ `WebCamTexture` | MRUK ＋ Meta XR Core SDK `[4]` | `Intrinsics`（FocalLength・PrincipalPoint・SensorResolution）と world pose が公式にある `[2]` | 採らない |
| C. Android Camera2 を直叩き | `AndroidJavaObject` ＋ `SurfaceTexture` | 無し（自作） | `CameraCharacteristics` の Meta vendor tag から取れる `[3]` | 控えのみ |

**A を採る理由は依存の形**です。B は Meta XR Core SDK を持ち込むので `OVR` 系の言及が
`Platform/<系>/` の外へ漏れる危険があり、`PlatformIsolationTests` の見張る線に触ります。**C も成立
はします**（権限自体が Camera2 のもので、Meta も Camera2 経由と明記 `[3]`。第三者パッケージ
UXR.QuestCamera が実例 `[10]`）が、Surface の受け渡しが自作になるので控えです。

### 1.2 A の到達点（版ごと）`[5]`

| 版 | 日付 | 何が入ったか |
|---|---|---|
| 2.4.0-pre.1 / 2.4.0 | 2025-10-10 / 12-05 | **CPU 画像取得**と描画行列／GPU 画像取得・High Fidelity Scene の plane provider |
| **2.5.0-pre.1 → 2.5.1（今ここ）** | 2026-02-03 → 06-22 | カメラ権限を **opt-in したときだけ** manifest へ入れる。**mono（左目）の CPU 画像まで**——**技術検証はこの版のまま始められる** |
| 2.6.0-pre.1 / 2.6.1 | 2026-04-12 / 08-20 | 左右の個別取得と同期ステレオ対／YUV → `RGBA32` 変換が約 **30% 高速化** |

### 1.3 API と設定

| 事項 | 内容 |
|---|---|
| 取得（mono＝左目） | `MetaOpenXRCameraSubsystem.TryAcquireLatestCpuImage()` `[6]` |
| 取得（左右個別／ステレオ対） | `TryAcquireLatestCpuImageForPosition(CameraPosition.LeftEye/RightEye)`・`TryAcquireLatestStereoCpuImagePair()`（**2.6.0-pre.1 以降**）`[6]` |
| 変換と後始末 | `XRCpuImage.Convert()`（同期・`RGBA32`／`Alpha8`／`R8`）／`ConvertAsync()`。**`Dispose()` 必須**——取りこぼすと AR プラットフォーム側がメモリ切れになる `[9]` |
| 有効化 | Project Settings → XR Plug-in Management → OpenXR → Android → Meta Quest →「Meta Quest: Camera (Passthrough)」の歯車 → **Camera Image Support** `[6][7]` |
| 権限 | manifest は `<uses-permission android:name="horizonos.permission.HEADSET_CAMERA" />`（上の opt-in で**自動で入る** `[1][5]`）。実行時は `UnityEngine.Android.Permission.RequestUserPermission(…)` を**自分で呼ぶ** `[10]` |
| Android API・GPU 画像 | **CPU 画像は minSdk 32（Android 12L）以上。** GPU 画像は Vulkan 必須・当該フレーム限り・**Quest Link では不可** `[6]` |

> **`AndroidPlayerSetup.MinimumSupportedSdk` が 26 のままです**（TLabWebView の要求）。カメラを入れる
> 版で **32 へ上げる**必要があります。Horizon OS v74 は Android 12L 基盤なので実害はありません。

### 1.4 画像の仕様（Meta 側の公表値）

| 項目 | 値 | 備考 |
|---|---|---|
| 対象機 | **Quest 3 / 3S のみ**・Horizon OS **v74 以降** `[1][2]` | Quest 2／Pro は不可 |
| 解像度・カメラ | 1280×960（HzOS v83 以降は 1280×1280 も）・前面の左右 RGB 2 つ・内部形式 YUV420 `[2][3]` | 認識には過剰。640×480 程度へ落とす。2.5.1 では左目のみ |
| フレーム・遅延 | 60Hz・撮影遅延 20〜40ms（Meta の overview `[2]`）。初期資料と報道では 30fps・40〜60ms `[3]` | **食い違うので実測する** |
| 負荷・画角 | 1 ストリームあたり GPU 約 **1〜2%**・メモリ約 **45MB**。画角は**利用者の視界より狭い矩形** `[2]` | 「見えているのに写っていない」が起きる |

### 1.5 落とし穴

| 落とし穴 | 内容 |
|---|---|
| Editor で動かない | XR Simulator はカメラ非対応 `[2]`。Editor では必ず Null 実装へ落ちる作りにする |
| 汎用 API では取れない | AR Foundation の platform support 表で Meta の "Camera image" は**非対応** `[8]`。subsystem をキャストして provider 固有の口を呼ぶ |
| 内部パラメータが表に無い | Meta は Display／Projection 行列は対応、`XRCameraIntrinsics` は載らない `[8]`。画像座標 → 世界座標が要るなら **(1) 行列で代用 (2) C で vendor tag を読む (3) B に寄せる** の三択 |
| 見た目とデータ扱い | 初回インストール直後に R/B が反転することがあり**再起動で直る**。カメラ画像は Meta の "Device User Data" で Developer Data Use Policy の対象 `[2]` |

## 2. 端末内の認識

### 2.1 Unity Inference Engine（旧 Sentis）

| 事項 | 内容 |
|---|---|
| 名前と版 | 2023 年 Sentis → Unity 6.2（2025-08）で Inference Engine → **2.4 で表示名は Sentis へ戻った** `[11]`。ID は `com.unity.ai.inference`、**2.6.1 が既に入っている**（追加導入不要） |
| バックエンド | `GPUCompute` 推奨／`CPU`。**NPU・ハードウェア加速は使わない** `[12]` |
| モデル寸と実行 | Meta は**最小版を推奨**（YOLO 8MB 級。サンプルの YOLO9t は量子化して約 2.3MB。**中版 146MB は成績が落ちる**）。「layer by layer」で `K_Layers Per Frame` ずつ複数フレームに割る `[12]` |
| 既知の粗さ | 枠が対象に**ぴったり合わない**。検出を 3D 枠に直す所で CPU/GPU 同期が入り引っかかる `[12][13]` |
| 実測値 | **Meta も Unity も Quest 3 の fps／熱／電池の数値を公表していない。** 自分で測るしかない |
| 公式の実例 | `MultiObjectDetection`（YOLO ＋ Inference Engine）。ただし **MRUK ＋ Meta XR Core SDK 前提** `[4][13]` なので、読んで真似る対象 |

### 2.2 台所の対象と COCO 80 クラスの対応 `[14]`

| 認識したいもの | COCO | 見込み |
|---|:--:|---|
| 鍋・フライパン・まな板 | **無し** | 鍋は `bowl` に寄る（深鍋は当たるが片手鍋は怪しい）。フライパンは該当無し、まな板は `dining table` に混ざる |
| コンロの火・湯気 | **無し** | 分類では出ない。火は輝度・色（HSV の橙領域）、湯気はフレーム差分の揺らぎの方が素直 |
| コンロ本体 | `oven`（近い） | レンジ／オーブンに反応する。「火が付いているか」は**分からない** |
| タイマーの表示 | **無し** | §2.3 |
| 食材 | `banana` `apple` `orange` `broccoli` `carrot` | この 5 種だけ。和食の材料はほぼ外れる |
| 器・道具・設備 | `knife` `bowl` `cup` `bottle` `wine glass` `fork` `spoon` `scissors` `microwave` `oven` `sink` `refrigerator` `toaster` `clock` | 有り。ただし包丁は調理中に手や食材で隠れる |

**素の COCO で足りるのは「器・設備・包丁」まで**で、**この計画が本当に欲しい「鍋・フライパン・
火・湯気」は 1 つも無い**——ここが端末内から始めない理由です。

### 2.3 文字認識（タイマー・計量）

端末内 OCR の標準機能は無く、PaddleOCR の超軽量モデル（約 8.6MB）を ONNX にして Inference Engine で
回すか、OpenCV for Unity（有償）を使う道になります `[15][16]`——**どちらも見送り**が妥当です。タイマーは
`Domain/CookTimer` が**自分で持っている**ので、外の表示を読む必要がありません。計量（はかりの表示）
だけは OCR の対象になりますが、必要なら PC 側で、かつ v2 の範囲外に置きます。

## 3. サーバ（manor）側の認識

### 3.1 送る量の設計

| 事項 | 案 | 根拠・理由 |
|---|---|---|
| 解像度・形式 | 640×480・JPEG q70（30〜60KB/枚） | 認識には十分。LLM に渡すなら 1.15MP 以下が推奨 `[17]` |
| 頻度 | 常時 **1 枚/2 秒**（0.5fps）、山場だけ 2fps・10 秒 | 工程の変化は秒単位。毎フレーム送る必要が無い |
| 遅延の見込み | 撮影 20〜40ms ＋ 変換・圧縮 ＋ LAN 往復 ＋ 推論 = **0.3〜1 秒** | 工程判定には十分。**手の安全には遅すぎる** |
| 通信が切れたら | 送らない・溜めない | 古い画像の判定は有害（`CookEventQueue` に積むのは進行だけ） |

### 3.2 PC 側の選択肢

| 選択肢 | 得意 | 不得意 |
|---|---|---|
| YOLO をローカル実行 | 常時・低遅延・無料・**画像が家から出ない** | クラスが固定（§2.2 の穴は埋まらない） |
| 自前学習（鍋・フライパン・火） | 台所の対象に効く | 撮影とラベル付けの手間 |
| **画像を LLM の視覚（Claude）へ** | 「鍋が火にかかっているか」を**言葉で訊ける**。クラス設計が要らない | 遅い（1〜3 秒）・従量（1000×1000 ≈ 1334 tokens `[17]`）・**画像が家の外へ出る** |

### 3.3 使い分けと、画像の扱い

1. **常時の見張りは PC のローカル YOLO**（0.5fps）。人・手・器の大まかな位置だけ取る。既定は
   **LAN の外へ出さない**。manor 側でも**画像は保存しない**（判定結果だけ残す）。
2. **「今の工程が終わったか」を判定したい瞬間だけ LLM**（＝必要な部分だけ LLM）。1 工程 1〜2 枚、
   利用者の明示の合図か、YOLO が変化を拾った時だけ。板に「今カメラが動いている」表示を出す。
3. **端末内推論は「画面に枠を出す」用**に限る（遅延が要る表示。判定には使わない）。
4. **火・手の安全に関わる警告は認識に依存させない**（§4）。カメラが落ちても警告は出る形にする。

## 4. ②カメラ無しの空間の注意表示

### 4.1 Scene から「コンロの領域」は取れるか — 取れない

Meta の意味ラベルは次の通りで、**コンロ・調理台・シンクのラベルは無い** `[18]`。

| 種類 | ラベル |
|---|---|
| Bounded 2D | `WALL_FACE` `CEILING` `FLOOR` `COUCH` `TABLE` `DOOR_FRAME` `WINDOW_FRAME` `WALL_ART` `OTHER` `INVISIBLE_WALL_FACE` |
| Bounded 3D | `COUCH` `TABLE` `STORAGE` `BED` `SCREEN` `LAMP` `PLANT` `OTHER` |

`STORAGE`（棚・戸棚）や `OTHER` に落ちるので**ラベルからコンロは同定できません**。なお AR
Foundation 側の対応は、平面（水平・垂直・分類）・3D 境界箱（分類付き）・メッシュのいずれも
**Unity OpenXR: Meta は対応**です `[19]`（`ARPlane.classification` はネイティブの**最初のラベル
1 つだけ**を持つ点に注意 `[20]`）。よって **配置モードを拡張して手で囲う**案を採ります——コンロの
領域（床の矩形＋高さ）を置いて保存する。台所に正確に合い、既存の配置モードの延長で済みます
（欠点は引っ越し・模様替えで再設定が要ること）。

### 4.2 保管 — `ArAnchorStore` だけでは足りない

`IAnchorStore` の口は `SaveAsync(string key, Pose pose)` で、**位置と向きしか持ちません**
（[`Platform/IAnchorStore.cs`](../../Assets/KitchenXR/Platform/IAnchorStore.cs)）。領域には広さが要ります。

| 要るもの | 今あるか | 案 |
|---|:--:|---|
| 領域の中心 Pose | ○ | `ArAnchorStore.SaveAsync("zone.stove", pose)`。鍵の追加だけ |
| 領域の寸法（幅・奥行・高さ） | **×** | `PanelPoseFile` と同じ作りの `zones.json` を足す（`Platform/ZoneFile`） |
| 注意板の位置 | ○ | 鍵 `panel.caution.<n>` を増やすだけ。`WorldSpacePanelFactory` と `PanelPlacement` はそのまま使える |

Editor の XR Simulation は Save／Load／Erase いずれも非対応なので、**必ず控えの側を通る**
（[`ARCHITECTURE.md`](../ARCHITECTURE.md) §5 と同じ）。

### 4.3 境界線と距離の案

| 距離 | 出すもの | 合図 |
|---|---|---|
| 60cm 以内（頭） | 床に琥珀の線を薄く出す（既存の強調色 `#F59E0B`） | 無音 |
| 40cm 以内（手） | 線を濃くする ＋ 立体の枠を腰高まで | 無音 |
| 20cm 以内（手） | 枠を赤へ。注意の札を 1 枚出す | 短い音 1 つ |

見るのは **XR Hands 1.8.1** の指先・手のひら関節と `Camera.main` の位置。評価は **10Hz** で足り、
**5cm のヒステリシス**で境界の点滅を止めます。表現は**床の線を基本**に（立体の枠は視界を塞ぐので
20cm 以内の短時間だけ）。**注意板は認識も領域も要りません**——既存の板の仕組みに鍵を足すだけで、
②の半分は今日の構成で作れます。

## 5. 技術検証の段取り

各段は**単独で止められる**こと（前段が壊れても後段が壊れない）を条件にします。

| 段 | 何を | 済みの印 | 要るもの | 落とし穴（1 行） |
|:--:|---|---|---|---|
| **a** | カメラ 1 枚をテクスチャに出す | 板に左目の絵が 1 枚出る | Camera Image Support・`minSdk 32`・実行時権限 | Editor では絶対に出ない（Null 実装を先に用意する） |
| **b** | 端末内 YOLO で枠を出す | 絵の上に枠と名前が出る | `com.unity.ai.inference` 2.6.1（**導入済み**）・YOLO の ONNX | 枠は対象にぴったり合わない `[13]`。`Dispose` 漏れでメモリ切れ `[9]` |
| **c** | manor へ送って札に出す | 板に PC 側の判定が文字で出る | manor 側の受け口 1 つ（`POST /api/v1/kitchen/vision/frames` 相当）・JPEG 化 | 送信で本体を止めない（非同期・失敗は黙って捨てる） |
| **d** | 「鍋が火にかかった」1 事象を送る | `CookSession.Apply` に `Observation` が 1 回届く | `Observation → Candidate → Stable → Completed` の実装 | **認識器は工程を進めない**（`completion: auto` はまだ使わない） |

**置き場所**（[`ARCHITECTURE.md`](../ARCHITECTURE.md) の規則を壊さないために）: `ICameraFrameSource`
の口は `Platform/`、実装 `PassthroughCameraFeed` は **`Platform/ArFoundation/` の中だけ**（AR
Foundation への言及はここだけ——`PlatformIsolationTests` が検算する線）、Editor 用の
`NullCameraFrameSource` は `Platform/Null/`、推論は新しい `Vision/`（板は判定結果しか見ない）、
manor への送信は既存の `ManorClient` の隣の `Net/`。

**最初の一手は a だけ**にしてください。a が実機で通れば「今の構成でカメラが取れる」という
この報告書の一番大きな仮定が確定し、b/c/d はどちらから進めても手戻りになりません。

### 段 a・b で同時に確かめること（残る不確かさ）

| 事項 | どう確かめるか |
|---|---|
| `XRCameraIntrinsics` が Meta で返るか | `TryGetIntrinsics` の戻り値を見る。返らなければ Display／Projection 行列で代用 |
| fps・遅延（60Hz/20〜40ms か 30fps/40〜60ms か） | タイムスタンプ差を実測 |
| 端末内 YOLO の熱・電池／ビルドへの影響 | 10 分連続で `adb shell dumpsys thermalservice` と電池残量。manifest の差分は `adb shell dumpsys package`（Internet permission が剥がれていないかも同時に） |

## 6. 出典

1. Passthrough Camera（Android apps） — https://developers.meta.com/horizon/documentation/android-apps/passthrough-camera/
2. Passthrough Camera API Overview（Unity） — https://developers.meta.com/horizon/documentation/unity/unity-pca-overview/
3. Passthrough Camera API overview（Spatial SDK） — https://developers.meta.com/horizon/documentation/spatial-sdk/spatial-sdk-pca-overview/
4. oculus-samples/Unity-PassthroughCameraApiSamples — https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples
5. Unity OpenXR Meta Changelog — https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/changelog/CHANGELOG.html
6. Image capture（Unity OpenXR Meta 2.6） — https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/manual/features/camera/image-capture.html
7. Camera (Passthrough)（Unity OpenXR Meta 2.5） — https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.5/manual/features/camera.html
8. Camera platform support（AR Foundation 6.5） — https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.5/manual/features/camera/platform-support.html
9. Image capture（AR Foundation 6.5） — https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.5/manual/features/camera/image-capture.html
10. UXR.QuestCamera Quick Start（第三者・Camera2 直叩きの実例） — https://uralstech.github.io/UXR.QuestCamera/DocSource/QuickStart.html
11. Inference Engine / Sentis の改名について — https://discussions.unity.com/t/did-inference-engine-package-revert-to-the-old-sentis-name/1695183
12. Unity Inference Engine For On-Device ML/CV Models（Meta） — https://developers.meta.com/horizon/documentation/unity/unity-pca-sentis/
13. Multi Object Detection sample overview — https://developers.meta.com/horizon/documentation/unity/unity-sample-camera-object-detection/
14. Microsoft COCO Classes Reference List — https://blog.roboflow.com/microsoft-coco-classes/
15. PaddleOCR with OpenCV for Unity — https://github.com/EnoxSoftware/PaddleOCRWithOpenCVForUnityExample
16. PaddleOCR 3.0 Technical Report — https://arxiv.org/html/2507.05595v1
17. Claude Vision & Multimodal Guide（画像のトークンと寸法） — https://likeone.ai/blog/claude-vision-multimodal-guide/
18. Semantic Classification for Scene — https://developers.meta.com/horizon/documentation/unreal/unreal-scene-supported-semantic-labels/
19. Bounding box / Plane detection platform support（AR Foundation） — https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/manual/features/bounding-box-detection/platform-support.html ／ https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@6.0/manual/features/plane-detection/platform-support.html
20. Planes（Unity OpenXR Meta 2.2） — https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.2/manual/features/planes.html

## 7. 実機の実測（2026-09-13）

Quest 3・`KitchenXR_v1.0.12-camera.apk`（段 a のビルド）を主人が実機で回した結果。
上の §1.4 の公表値と §3.1 の見積りに対する、初めての実数。

| 事項 | 実測 | 見積り（§1.4・§3.1） |
|---|---|---|
| 画像の寸法 | **640×640** | 1280×960 を 640 幅へ縮める前提だった |
| 取得〜表示 | **22ms** | — |
| JPEG（q70）の大きさ | **20〜24KB** | 20〜40KB（当たり） |
| 連写 2fps の内訳 | **取得 20ms ＋ 変換 15ms** | 1枚 35ms なら 2fps に十分な余裕 |
| 絵の向き | **180 度回転していた** | `MirrorY` だけを掛けていた |
| 権限を許した直後 | **そのセッションでは 1 枚も取れない。立ち上げ直すと取れる** | 想定していなかった |

分かったこと3つ:

1. **寸法は 640×640。** 1280×960 の 4:3 ではなく正方形で来る。`MaxOutputWidth = 640` は
   結果として効いていない（元から 640 幅）。認識に渡す量としては §3.1 の想定どおり。
2. **反転は `MirrorX` が正しい。** 180 度回転 ＝ 上下＋左右の反転。`MirrorY(元)` が
   180 度回って見えたということは `MirrorY(元) = Rot180(真)` で、両辺に `MirrorY` を掛けると
   `元 = MirrorY(Rot180(真)) = MirrorX(真)`、すなわち **`真 = MirrorX(元)`**。
   `Platform/MetaCamera/MetaOpenXRPassthroughCamera.OutputTransformation` を `MirrorX` にした。
   左右が合っているかは実機でしか見えないので、札に「反転: X」を出している。
3. **権限の直後はカメラの口を起こし直す。** 権限が無いまま始まった `XRCameraSubsystem` は
   そのセッションの間ずっと 1 枚も返さない。許可が下りたら `ARCameraManager` を
   disable→enable（無ければ subsystem を Stop／Start）して 0.6 秒待ち、それでも来なければ
   0.4 秒おきに 4 回まで取り直す。そこまでして駄目なら札に
   「許可されました。もう一度「カメラ」を押してください」と出す（2 度目は必ず取れる）。

速さは十分——取得 20ms ＋ 変換 15ms なら 2fps（500ms 間隔）に対して 7% しか使っていない。
段 (b) の端末内認識（§2.1）に回せる時間が 400ms 以上あるということ。

### 7.1 追記——権限の直後に `ARCameraManager` を無効化するとパススルーが消える（実機）

上の 3 番（「権限の直後はカメラの口を起こし直す」）は**取り消し**。`KitchenXR_v1.0.12-camera2.apk`
で試したところ、**カメラの権限を許したその瞬間に MR の視界が真っ暗になり、戻らなかった**。
カメラ自体は取得に成功していたので、消えていたのは映像ではなく**パススルーの背景**。

Unity OpenXR: Meta では**パススルーの描画が `ARCameraManager` に結び付いている**（背景を描くのは
`ARCameraBackground` で、その絵は `ARCameraManager` から来る）。`enabled` を落とした時点で背景が
消え、`enabled` を戻しても復帰しませんでした。1 枚も取れないより**背景が黒い方がまずい**——
調理中は視界そのものなので。

したがって:

- **`ARCameraManager` にも subsystem にも触らない。** `Restart()` は口ごと落とした
  （`IPassthroughCamera` から外し、`EditMode` の `PlatformIsolationTests` で
  「`MetaOpenXRPassthroughCamera` のコードに `enabled =`・`ARCameraManager`・`.Stop()`・`.Start()`
  が現れない」を静的に縛っている。実機でしか現れない壊れ方なので、机の上ではこれしか縛れない）。
- **権限は起動時に求める。** 場面が読まれる前（＝AR セッションが立つ前）に
  `Permission.HasUserAuthorizedPermission("horizonos.permission.HEADSET_CAMERA")` を見て、
  無ければ `RequestUserPermission`（`Bootstrap.RequestHeadsetCameraPermissionAtStartup`。
  `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`）。manifest に権限が入っているビルド
  ——すなわち Camera Image Support を立てたビルド——でだけ呼ぶ
  （`MetaOpenXRPassthroughCamera.IsHeadsetCameraDeclared()`。`PackageManager` の
  `requestedPermissions` を見る）。**許可済みなら最初のセッションからカメラが動く。**
- 許可した直後のセッションについては諦める。「カメラ」を押したときは 0.4 秒おきに 4 回まで
  取り直し、それでも取れなければ札に**「許可されました。アプリを立ち上げ直してください」**。

言い換えると、「後から許して今のセッションで動かす」道は塞がっている。開いているのは
「起動時に訊いて、次の起動から動かす」道だけ。
