# 構成（層・板・入力・保管）

KitchenXR の中身がどう分かれていて、どこに何が書いてあるかをまとめます。
設計の理由は [`DESIGN.md`](DESIGN.md)、manor との連携は [`MANOR.md`](MANOR.md) に分けてあります。

## 1. 層と依存の向き

```mermaid
graph LR
  App[App<br/>Bootstrap] --> Pres[Presentation<br/>板・入力・配置]
  App --> Net[Net<br/>manor の口・端末内の保管]
  App --> Plat[Platform<br/>アンカー・パススルー・手]
  Pres --> Dom[Domain<br/>UnityEngine 非依存]
  Net --> Dom
  Plat --> Dom
  Pres -.->|Presentation/Video だけ| Web[TLabWebView]
  Plat -.->|Platform/ArFoundation だけ| AR[AR Foundation]
  Plat -.->|Platform/MetaCamera だけ| Cam[Unity OpenXR: Meta<br/>パススルーカメラ]
```

| 層 | 置き場 | 持つもの |
|---|---|---|
| Domain | `Assets/KitchenXR/Domain/` | `Recipe`・`Step`・`Phase`・`Ingredient`・`CookSession`（工程の状態機械）・`CookTimer`・`RecipeJson`／`RecipeListJson`／`MediaJson`（読み取り） |
| Platform | `Assets/KitchenXR/Platform/` | `IAnchorStore`・`IPassthroughControl`・`IHandInputPolicy`・`IPassthroughCamera` の口と、`ArFoundation/`・`MetaCamera/`・`Null/` の実装、`PanelPoseFile`・`CameraFrame` |
| Net | `Assets/KitchenXR/Net/` | `ManorClient`・`ManorDiscovery`・`ManorSettings`・`ManorDeviceFile`・`RecipeStore`・`MediaStore`・`CookEventQueue`・`LastSessionStore` |
| Presentation | `Assets/KitchenXR/Presentation/` | 板（UI Toolkit）・`PokePress`・`PanelPlacement`・`WristMenu`・`FingertipCursor`・`Video/`・`CameraProbe`（カメラの下見） |
| App | `Assets/KitchenXR/App/` | `Bootstrap`（どのアダプタを挿すか）・`FileLog`・`Editor/`（シーン生成・ビルド） |

守っている規則は4つで、いずれも `Tests/EditMode/PlatformIsolationTests.cs` が検算します。

- **Domain は UnityEngine に依存しない。** `KitchenXR.Domain.asmdef` は
  `noEngineReferences: true`（参照は Newtonsoft.Json だけ）なので、構造として破れません。
- **機種・XR SDK 固有の呼び出しは `Platform/<系>/` の中だけ。**
  `UnityEngine.XR.ARFoundation`・`OVR`・`PXR` への言及がその外に出たら試験が落ちます
  （`Platform/PanelPoseFile.cs` や `Platform/Null/` も「外」です）。
- **WebView に触るのは `Presentation/Video/` の中だけ。** 板は `IVideoPlayer` の口しか見ません
  （WebView は Android にしか無いので、Editor では `NullVideoPlayer` に差し替わります）。
- **パススルーカメラの SDK に触るのは `Platform/MetaCamera/` の中だけ。**
  `UnityEngine.XR.OpenXR.Features.Meta`・`MetaOpenXRCameraSubsystem`・`XRCpuImage` への言及が
  その外に出たら試験が落ちます（§8）。

アセンブリは `KitchenXR.Domain`・`KitchenXR.Runtime`・`KitchenXR.App.Editor`・
`KitchenXR.Tests.EditMode`・`KitchenXR.Tests.PlayMode` の5つです。

## 2. 板の一覧

すべてワールド空間の UI Toolkit パネルで、`Presentation/WorldSpacePanelFactory` が一本の経路で
組み立てます（シーン生成と PlayMode 試験が同じ経路を通ります）。`PanelSettings` は World Space・
Pixels Per Unit 100、板の `localScale` は 0.2 なので **1 UI px = 2mm** です。

| 板 | 実装 | 大きさ | 何を持つか |
|---|---|---|---|
| 一覧 | `RecipeListPanel` | レシピの板と同じ | 3列のカード（写真・題名・分／分類／kcal）、設定の区画、ペアリング番号の覆い |
| レシピ | `RecipePanel` | 260×190 px ≒ 52×38cm | 工程の点列と進捗%、左に工程画像（正方形）、右に見出し・説明・材料の札・「次: …」、下に `[一覧へ][配置] … [戻る][次へ]` |
| 材料 | `IngredientsPanel` | 170×240 px ≒ 34×48cm | チェックの行（縦のみ。16 点までスクロール無し）。今の工程で使う材料を強調 |
| タイマー | `TimerPanel` | 220×220 px ≒ 44×44cm | 1/3/5/10 分と ±30 秒で作り、3つまで同時に動く。終了は合成音と点滅 |
| 動画 | `Presentation/Video/VideoPanel` | 16:9 は 354×224 px、9:16 は 206×264 px | 左に窓（WebView）・右に再生リスト（100 px 幅は向きで変えない）・下に操作部と札3行。一覧側の上にカメラの下見（10cm 角の窓と釦2つ。§8） |
| 手首メニュー | `WristMenu`・`WristMenuButton`・`PlacementMenuPanel` | 釦 22×22 px ≒ 4.4cm 角／メニュー 130×92 px | 調理中は「配置」1つ、配置モード中は「保存」「元に戻す」「板を手元に」「やめる」 |

一覧とレシピは**同じ場所に置かれる2枚**で、アンカーの鍵も共有します（`panel.recipe`）。

## 3. 入力

- **ポークは押し下げで発火。** `Presentation/PokePress` が `PointerDown` に反応し、押し上げは
  見ません（手応えの無いホログラムは深く突き抜けるので、押し上げが板の中で起きません）。
  二重発火は `ClickDebounce`（600ms）と「その指が板から離れるまで次を受けない」で止めます。
  板の当たり判定の箱は**裏側にだけ** 24cm 伸ばしてあり、深く突き抜けても指が抜けません。
- **レイは層で絞る**（`Presentation/CookingModeInputGate`）。Near-Far Interactor は
  どちらのモードでも生かしたまま、届く先を2つの層で決めます。
  - Interaction Layer（XRI の 1 番 = `Video`）: 調理モードの Ray は `Video` だけ、配置モードは全部。
  - 物理層（8 番 `Kitchen Panel Off Ray`）: Interaction Layer は UI Toolkit の当たりには効かない
    ため、調理モードの間は動画以外の板をこの層へ移します。Ray の `raycastMask` からは外れ、
    `Physics.DefaultRaycastLayers` には残るので**指では押せてレイでは押せない**状態になります。
  - `RayLineVisibility` がホバー・選択のある間だけレイの線と `LineRenderer` を出します。
- **配置モード**（`PanelPlacement`）: Ray＋Grab で板を掴んで動かします。同じ鍵に重なった板は
  **見えている側を取っ手役**に選び、残りは引っ込めます。出口は手首メニューにしかないので、
  メニューが挿さっていない／手が1つも追えていないときは配置モードへ入りません。
- **指先カーソル**（`FingertipCursor`）: 板に 5cm 以内へ近づいたときだけ、人差し指の先に直径 8mm の
  球を出します。近いほど大きくして距離計にし、押した瞬間だけ一瞬強く光らせます。左右の手と
  コントローラの Poke Interactor 4つすべてに付きます。
- **効果音**（`PressSound`）: ボタンが発火した瞬間に短い音を1つ鳴らします。

## 4. オフライン優先（端末内の保管）

表示は**常に端末内の写しから**行い、サーバへの報せは待ち行列に積むだけです。すべて
`Application.persistentDataPath` の下に置きます（Quest 3 では
`/sdcard/Android/data/<applicationId>/files/`）。

| 置き場 | 実装 | 役割 |
|---|---|---|
| `recipes/index.json`・`recipes/<id>/recipe.json`・画像 | `Net/RecipeStore` | 一覧とレシピの写し。レシピを選ぶと契約 JSON と画像（hero＋全工程）を**先に全部**落としてから調理を始める |
| `media.json`（控えは `media.bundled.json`） | `Net/MediaStore` | 動画リストの写し。同梱の `StreamingAssets/media.json` は見本 |
| `media-thumbs/<video_id>.jpg` | `Presentation/Video/MediaThumbnailCache` | 再生リストのサムネイル。id ごとに決まるので一度取れば通信しない |
| `cook_events.jsonl` | `Net/CookEventQueue` | 進行（`next`／`prev`／`end`）の待ち行列。積んだ順に送り、送れた分だけ消す |
| `last_session.json` | `Net/LastSessionStore` | 最後に開いていた調理。manor に繋がらないときの復帰に使う |
| `settings.json` | `Presentation/DisplaySettings` | 文字と板の大きさ（小・中・大）。壊れていれば黙って既定に戻す |
| `logs/kitchenxr.log` | `App/FileLog` | 1MB で3世代まで回す |

`Bootstrap` の起動順は「待ち行列を流す → 途中の調理があれば復帰 → 無ければ一覧 →
選んだら手元へ先読み → 調理の板へ」です。**送れないことで調理は止まりません。**

## 5. アンカー（板の置き場所を覚える）

- `Platform/ArFoundation/ArAnchorStore` が AR Foundation の永続アンカーを使います
  （`TryAddAnchorAsync` → `TrySaveAnchorAsync` の `SerializableGuid` を `anchors.json` へ。
  復元は `TryLoadAnchorAsync`）。Saved Anchor Ids の列挙は非対応なので、GUID は自分で持ちます。
- **鍵は板1枚＝1つ**（`panel.recipe`・`panel.ingredients`・`panel.timer`・`panel.video`）。
  台所全体の座標系は作りません（アンカーは近いほど精度が高い）。
- **控え `panels.json`**（`Platform/PanelPoseFile`）に XR Origin 基準の相対 Pose を**必ず一緒に**
  書きます。部屋が変わればアンカーの復元は普通に失敗するので、受け皿が要ります。
- 復元の順は アンカー → 控え → 既定（頭の前 0.8m）。戻した板は 0.6 秒かけて薄く出します。
- アンカーも控えも位置と向きしか持たないので、**縮尺は起動のたびに `settings.json` から当て直します**。
- Editor の XR Simulation は Save／Load／Erase のどれも非対応なので、必ず控えの側を通ります。

> **板を置き直す手順**: ①手首の釦を反対の手で押してメニューを出し「配置」を押す
> （レシピ／一覧の板の頭の「配置」2度押しでも入れます）。②全部の板に琥珀の枠と取っ手が出るので、
> レイで指して掴んで置き直す（遠くからでも引き寄せられます）。奥へ行ってしまったら
> 「板を手元に」で初期配置へ戻します。③「保存」で覚えて調理モードへ戻る（「元に戻す」は
> 入る前の位置へ、「やめる」は戻して抜ける）。配置モードの間、調理の板のボタンは効きません。

## 6. 動画の板（WebView を包む）

- `Assets/TLab/Youtube/Prefab/YoutubePlayer.prefab` を**書き換えずに包みます**。絵は
  プレハブのワールド空間 Canvas ＋ RawImage をそのまま「窓」の手前に重ねます——UI Toolkit の
  `backgroundImage` に貼り替えると、動的アトラスに焼かれて外部テクスチャの更新が届かず
  最初の1枚で固まります（実機でしか出ない止まり方）。当たり判定は UI 側の板だけが持ちます。
- `YoutubePlayer.cs` は asmdef の無いフォルダにあり `Assembly-CSharp` に入るため、
  `YoutubePlayerBridge` が**型の名前で引き当てて**呼びます（`TLabWebView` は asmdef を持つので
  型でそのまま呼べます）。
- **窓そのものに触れます。** `VideoPanel` が絵の中の比（u, v）に写して
  `IVideoPlayer.Touch(phase, u, v)` へ流し、`YoutubePlayerBridge` が解像度を掛けて
  `TLabWebView.TouchEvent` を送ります。窓は釦ではないので `PokePress` は通しません。
  - タップとドラッグは**板の側で見分ける**。動きが絵の幅の 3% を越えるまで DOWN を送らず、
    越えないまま 1.2 秒以内に離れたらタップ（DOWN → 80ms → UP を同じ座標で）。
    これをしないと手の震えが DRAG になり、WebView はスクロールと解釈してクリックを出しません。
  - 関連動画が `youtube.com/watch` へ遷移したら（0.5 秒ごとに URL を見ている）、
    URL から video_id を抜いて html を読み直し、埋め込みプレイヤーへ連れ戻します。
  - 読み込み後と向きの切り替えごとに、`html`・`body` の上でだけ `touchmove` を止める JS を送ります
    （iframe の中は素通し。html そのものは書き換えません）。
- 板の札は3行——中の様子と絵の実寸／板が最後にしたこと／最後の失敗（赤）。
  実機で切り分けるための唯一の窓口です。

## 7. 表示の設定

一覧の板の「設定」で、一覧と**入れ替わりに**設定の区画が出ます（板は増やしません）。
「文字の大きさ [小][中][大]」「板の大きさ [小][中][大]」「閉じる」の3行だけで、押した瞬間に
反映して保存します（「決定」は置きません）。

- **文字**: `DisplaySettingsApplier` が板の `.root-panel` に `font-scale--small` /
  `font-scale--large` を付け外しし、`theme.uss` がその配下で `--font-size-*` を上書きします
  （小 ×0.85・大 ×1.25）。カスタムプロパティは継承するので中の要素も一緒に動きます。
- **板**: `localScale` を `WorldSpacePanelFactory.PanelLocalScale` × 係数（0.85／1.0／1.2）に
  します。コライダーはローカル単位なので一緒に拡縮され、押せる場所もずれません。
- 当てる先は一覧・レシピ・材料・タイマーの4枚。動画の板は自分で寸法を決める（16:9 ⇄ 9:16）ので
  混ぜません。手首メニューと配置の操作も、出ている間だけの板なので対象外です。
</content>

## 8. カメラの下見（`Platform/MetaCamera`）

ロードマップ v1-d の段 (a)——**Quest 3 のカメラから1枚もらって板に出せるか**だけを確かめる
仕掛けです。認識も判定もしません（段 (b)(d) の仕事）。根拠は
[`research/2026-09-13_quest3-camera-and-recognition.md`](research/2026-09-13_quest3-camera-and-recognition.md)
の §1・§5。

### 8.1 取る

- 口は `Platform/IPassthroughCamera`（`IsSupported`・`RequestPermissionAsync`・
  `TryAcquire`・`AcquireAsync`・`PermissionJustGranted`・`TransformationText`・`Dispose`）で、
  返すのは `CameraFrame`（画素・寸法・撮影時刻・内部パラメータ）だけ。
  板も `CameraProbe` も SDK の型を見ません。
- 実装は `Platform/MetaCamera/MetaOpenXRPassthroughCamera`。**AR Foundation の汎用 API
  （`ARCameraManager.TryAcquireLatestCpuImage`）では取れません**——対応表で Meta の
  "Camera image" は非対応なので、provider 固有の `MetaOpenXRCameraSubsystem` を
  `SubsystemManager` から引いて直に叩きます。Start／Stop は rig の `ARCameraManager` が持つので、
  ここでは**見つけるだけ**（持ち主を2つにしない）。
- 変換は `XRCpuImage` の中で終わらせます。YUV420 → `RGBA32`、長辺を **640** まで縮小、
  反転は **`MirrorX`**。実機では `MirrorY` だと絵が 180 度回っていました
  （実測 2026-09-13。`MirrorY(元) = Rot180(真)` ⇒ `真 = MirrorX(元)`。調査 §7）。
  左右が合っているかは実機でしか見えないので、札に「反転: X」を出しています。
  **`Dispose` は必ず通す**——取りこぼすと AR プラットフォーム側がメモリ切れになります。
- **権限は起動時に求めます**（`Bootstrap.RequestHeadsetCameraPermissionAtStartup`。
  `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` なので**場面が読まれる前＝AR セッションが
  立つ前**）。権限が無いまま始まった subsystem はそのセッションの間ずっと 1 枚も返さないので、
  許可済みで起動するのが唯一カメラが動く道です。呼ぶのは manifest に権限が入っているビルド
  ——Camera Image Support を立てたビルド——だけ（`IsHeadsetCameraDeclared`）。
- **口は起こし直しません。`ARCameraManager` にも subsystem にも触りません。**
  Unity OpenXR: Meta ではパススルーの描画が `ARCameraManager` に結び付いているので、
  権限の直後に disable→enable したら **MR の視界が真っ暗になって戻りませんでした**
  （実測 2026-09-13。調査 §7.1）。1 枚も取れないより背景が黒い方がまずい。
  この壊れ方は実機でしか現れないので、`PlatformIsolationTests` が
  「`MetaOpenXRPassthroughCamera` のコードに `enabled =`・`ARCameraManager`・`.Stop()`・`.Start()`
  が現れない」を静的に縛っています。
- 許可した直後のセッションは諦めます——`CameraProbe` が 0.4 秒おきに 4 回まで取り直し、
  それでも駄目なら札に「許可されました。アプリを立ち上げ直してください」と出します。
- 連写は `ConvertAsync` の道（`AcquireAsync`）を通り、変換で主スレッドを止めません。
- Editor は必ず `Platform/Null/NullPassthroughCamera` に落ちます（XR Simulator はカメラ非対応）。
  差し替えは `PassthroughCameraFactory`（`AnchorStoreFactory` と同じ役回り）。

### 8.2 設定（3つだけ。追加パッケージは要りません）

| 事項 | どこ | 効き目 |
|---|---|---|
| **Camera Image Support** | OpenXR →「Meta Quest: Camera (Passthrough)」。`AndroidPlayerSetup.ApplyCameraImageSupport` が feature id と `SerializedObject` で立てる | 画像取得が有効になり、**manifest に `horizonos.permission.HEADSET_CAMERA` が入る**（2.5.0 以降は opt-in したときだけ入る）。だから `Plugins/Android/AndroidManifest.xml` は要らない |
| **minSdk 32** | `AndroidPlayerSetup.CameraMinimumSdk` | CPU 画像は Android 12L 以上。WebView の 26 と別に持ち、厳しい方を使う |
| **実行時の権限** | `MetaOpenXRPassthroughCamera.RequestPermissionAsync` | manifest に在るだけでは足りない。`Permission.RequestUserPermission` を自分で呼ぶ |

型（`ARCameraFeature`）ではなく feature id で引くのは、カメラの SDK への参照を
`Platform/MetaCamera/` の外へ出さないためです（§1 の4つ目の規則）。
検算は `KitchenSceneIntegrityTests`。

### 8.3 出す（動画の板の一覧側）

- 「カメラ」の釦を押すと1枚取り、**10cm 角（50 UI px）の小さな窓**に `Texture2D` を
  `backgroundImage` で貼って、札に
  `取得 640×640 / 取得〜表示 22ms / 内部パラメータ: あり / 反転: X` を実測で出します
  （数字は 2026-09-13 の実測。調査 §7）。もう一度押すと消えます。
  失敗（非対応・権限拒否・null）は理由をそのまま札へ。
- **釦も窓も一覧側に置きます。** 動画の絵は板の 1cm 手前に浮いた uGUI の `RawImage`（§6）
  なので、窓の中へ重ねると必ずその裏に隠れます。窓は絵を出している間だけ開き、
  畳んでいる間は再生リストの行を食いません。
- 動画の板を選んだのは、**調理中も押せる唯一の板**だから（`CookingModeInputGate` の
  `Video` 層。§3）。手首メニューの「配置」の隣に置くと、配置モードに入らないと押せません。
- 実測は札と **`FileLog`** の両方へ出ます。USB で繋がずに確かめる段なので、後から読む場所が要ります。

### 8.4 (c) の下見（JPEG 化と送信の負荷）

- 「連写2fps」で 2枚/秒。取得 → `EncodeToJPG` 相当（`ImageConversion.EncodeArrayToJPG`・品質 70）
  までの **KB と ms** を札に流します。JPEG 化は別スレッドで回し、Unity に弾かれたら
  主スレッドでやり直します（黙って落とさない）。
- manor の繋ぎ先と鍵があれば、**1枚だけ** `POST /api/v1/kitchen/vision/frame`
  （`image/jpeg`・Bearer は既存の `ManorClient` の経路）へ投げて、往復 ms と HTTP 状態を出します。
  **manor 側の受け口はまだ無いので 404 が正常**——測りたいのは時間だけなので、それで足ります。
