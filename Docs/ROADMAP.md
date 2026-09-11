# kitchen-xr — ロードマップ（プロトタイプ）

設計は `Docs/design/PROTOTYPE.md`。段は**動くものを1つずつ**足す順に並べ、各段に「済みの印」を
置く（manor の ROADMAP と同じ流儀）。期間は主人が夜と週末に触る前提の目安。

## 0. 前提

- 機材: Meta Quest 3（初期の唯一の標的）。PICO・WebXR は口だけ切る
- 道具: Unity 6.3 LTS（6000.3 系。Mixed Reality テンプレート・UI Toolkit）＋ OpenXR ＋ XRI 3.x ＋ AR Foundation 6／Python 3.12 ＋ FastAPI ＋ SQLite（manor と同じ）
- 置き場: `C:/Users/machi/Documents/UnityProjects/KitchenXR/`（`Assets/`・`Docs/`）
- サーバ: **AI Manor**（料理長のレシピ帳。manor の ADR-015）。XR は読む側
- 動画: 主人の WebView SDK（unitypackage）
- 進め方: 設計判断は執事、実装は AI（Sonnet 等）へ委譲、主人は実機で触って直す点を言う
- **依存は kitchen-xr → manor の一方向のみ。** manor 側のレシピ帳は manor の ADR-015 として別に進める（XR 専用の口は作らない）

## 1. 段取り

| 段 | 何を | 済みの印 | 目安 |
|:--:|---|---|:--:|
| **P0 骨組み** | ①主人: Hub で Mixed Reality テンプレートの `KitchenXR` を作る（§2）。②執事: `Docs/` へ文書を移す・Claude Code の Unity プラグイン導入・パッケージ（UniTask・Newtonsoft・Noto Sans JP の Font Asset・Android Logcat。演出のライブラリは入れない）・`Platform/` の3つのインターフェースと Null／ArFoundation アダプタ・EditMode 試験の枠・manor の `GET /api/v1/kitchen/recipes`（ADR-015 R1）を叩く `KitchenApi.cs` の骨・git init。③主人: WebView SDK の unitypackage を入れる | Quest 3 でパススルーの中に空のパネルが1枚出る。Editor から manor の `/api/v1/health` が読める | 1週 |
| **P1 手動進行** | 見本 `Docs/samples/chahan.recipe.json` をローカルに置き、レシピ／材料／タイマーの3パネル。`CookSession`（次へ／戻る／進捗）。UI Toolkit の `theme.uss`（明るい白・琥珀）。調理モードの Poke と誤操作の歯止め（§7） | 実機でレシピ1本を最後まで**触って**進められる。`CookSession` の EditMode 試験が緑。**ワールド空間 UI Toolkit の触り心地を判定**（悪ければ uGUI へ） | 2週 |
| **P2 アンカー** | `ArAnchorStore`（AR Foundation の永続アンカー）。配置モード（Ray＋Grab）と保存。起動時の復元。手動キャリブレーションの退避路 | アプリを落として再起動しても3パネルが同じ場所に戻る（3日連続で確認） | 1週 |
| **P3 レシピ帳と結ぶ** | manor 側の ADR-015 R1〜R3（表・API・取り込み・画面）が先。クライアントは passcode ログイン（cookie 保持）・一覧画面・取得・`cook-sessions` | manor の Web で炒飯を URL から登録し、Quest で一覧から開いて進められる | manor 側 1〜2週＋XR 側 3日 |
| **P4 動画** | 動画パネル（主人の WebView SDK）。`StreamingAssets/media.json` の一覧から開く（書き方は `Docs/media-json.md`）。16:9／9:16 の切り替え。文字入力は置かない | 料理しながらショーツが流れる | 1週 |
| **P5 復帰** | `cook-sessions/current` で途中起動の復帰。終了で `times_cooked` が増える | 途中で外して再装着しても同じ工程から続く | 2日 |
| — | **v0 完了。主人が2週間、実際の料理で使う。** 直す点を集める | 使用記録が10回を超え、「毎日使える」と主人が言う | 2週 |
| **v1-a 音声** | 音声で「次」「戻る」「タイマー3分」。OS の音声認識（Android SpeechRecognizer）から始め、精度が足りなければ Whisper（サーバ） | 手を洗わずに工程を進められる | 1〜2週 |
| **v1-b 料理長** | 「代わりの材料は？」を聞ける。manor の料理長を `claude -p`（`manor talk chef` 相当）で呼ぶ薄い口をサーバに置く（manor 本体は変えない） | 実機で質問して答えが声で返る | 1週 |
| **v2 認識** | Passthrough Camera API ＋ Unity Sentis（YOLO 系の軽量モデル）で「鍋に入れた」「タイマー終了」等の**観測イベント**を `CookSession.Apply` へ。`completion: auto/confirm` の使い分け。Observation→Candidate→Stable→Completed | 認識器を止めても手動進行が壊れない（分離の試験）。auto 工程が誤って進んだ回数を記録できる | 以降 |
| **v2+ 差し替え** | PICO アダプタ／WebXR（TypeScript＋Three.js。Unity の WebGL 書き出しは使わない） | `Platform/` の外に機種固有の呼び出しが無いことの検算が緑のまま | 以降 |

## 2. 最初の一歩（主人の手。3分）

1. Unity Hub → Installs で **6.3 LTS** を入れる → New project → Editor **6.3 LTS** → テンプレート **Mixed Reality**（無ければ
   Hub の「Download template」で取る）→ Project name **KitchenXR** → Location
   `C:/Users/machi/Documents/UnityProjects` → Create
2. 一度エディタが開いてパッケージの解決が終わったら閉じる（執事がそのあと `Docs/`・`Server/`・
   パッケージ・プラグインを入れる）
3. Quest 3 を開発者モードにし、USB で `adb devices` に出る状態にしておく

Claude Code の Unity プラグイン（公式。skill・Unity CLI・Editor を動かす MCP）は、プロジェクトが
できたらそのフォルダで `claude plugin install unity@unity-agent-plugin --scope local`
（marketplace `Unity-Technologies/unity-agent-plugin` は登録済み）。

## 3. 現在地

- 2026-09-11: 設計 v0（`Docs/design/PROTOTYPE.md`）とこのロードマップを作成。主人の答えで採択（§10）。
  **次: 主人が Hub で `KitchenXR` を作る（§2）→ 執事が P0 の残りを進める。** 文書は作成後に
  `AI Agents/kitchen-xr/` から `KitchenXR/Docs/` へ移す
- 2026-09-12: **P0 の残り**（Platform の3インターフェースと Null アダプタ・EditMode 試験の枠・
  Platform 隔離の検算試験）と **P1 一式**を実装（AI・エディタ非対話）。`Assets/KitchenXR/` に
  Domain（Recipe/Ingredient/Phase/Step・RecipeJson・CookSession・CookTimer）・Platform
  （IAnchorStore/IPassthroughControl/IHandInputPolicy と Null 実装）・Presentation（theme.uss・
  RecipePanel/IngredientsPanel/TimerPanel の UXML+USS+C#・調理モードの Poke 限定ゲート）・App
  （Bootstrap）を追加。MR テンプレートの SampleScene を複製して `Assets/KitchenXR/Scenes/Kitchen.unity`
  を作り（チュートリアル UI の `UI` ルートとサンプルの `Goal Manager`／`Object Spawner` を除去、
  AR Session・XR Origin・手はそのまま）、レシピ／材料／タイマーの3枚のワールド空間 UI Toolkit
  パネルと Bootstrap を配置。EditMode 試験24件が緑（`unity test`）。Android の APK ビルドを1回
  試して成功（`unity build`。IL2CPP・約99MB。ライセンスの警告のみでコンパイルエラー0）。
  `applicationIdentifier.Android` を `com.kazuhiro.kitchenxr` に変更。Build Settings の先頭に
  Kitchen.unity を追加（SampleScene は残す）。
  **ずらした点**: manor の `/api/v1/kitchen/recipes`（Net 層・`KitchenApi.cs`）は今回の指示の
  作業範囲外だったため見送り——P1 は設計 ROADMAP どおりローカル JSON（`Resources/Recipes/chahan.json`）
  で進行できるので実害は無いが、manor 側 ADR-015 が固まったら P3 として別途着手が要る。
  ワールド空間パネルの実寸（4cm角ボタン等）は XRI 公式サンプルの比率から逆算した概算
  （`theme.uss` 冒頭のコメント参照）——**実機で見た目を確認し、違えば localScale か USS の
  数値を直すこと**（これが P1 の「ワールド空間 UI Toolkit の触り心地の判定」そのもの）。
  **主人が実機で確かめる手順**: ① Quest 3 を USB 接続し開発者モードを確認 → `adb devices`。
  ② Unity Editor で `KitchenXR/Assets/KitchenXR/Scenes/Kitchen.unity` を開き、File > Build Settings
  で Android・Kitchen.unity が先頭にあることを確認して **Build And Run**（初回は時間がかかる）。
  または CLI ですでに作った `Build/KitchenXR.apk` を `adb install -r Build/KitchenXR.apk` で
  直接入れてから手動起動。③ パススルーの中にレシピ／材料／タイマーの3枚が頭の前0.8mに出て、
  指で触れて「次へ」「戻る」が反応し、材料チェックが押せ、タイマーが動くかを確認。手を伸ばした
  ピンチ操作やレイでボタンが反応**しない**ことも確認（調理モードの誤操作防止・設計 §7）。
- 2026-09-12（実機確認後の直し。v1.0.1）: 主人が Quest 3 で見つけた3点を原因まで切り分けて修正。
  **①文字が出ない**: `theme.uss` の `-unity-font-definition` が TMP の FontAsset を指しており、
  USS 取り込みが「Unsupported type TMP_FontAsset … only the following types are supported: Font, FontAsset」
  の警告とともにフォント指定を捨てていた（UI Toolkit は `UnityEngine.TextCore.Text.FontAsset` のみ）。
  TTF から TextCore 版（Dynamic）`Presentation/UI/Fonts/NotoSansJP-Regular UITK.asset` と
  `KitchenTextSettings.asset` を作って結び直し、併せて単位無しだった USS の長さ変数に `px` を付けた
  （`var()` 越しでは単位無しは無視され font-size が既定の 14px に戻っていた）。
  **②パススルーが効かない**: MR テンプレートの `Environment` ルート（グリッドの床と空）を消し忘れ。
  本来はチュートリアル UI のトグルが消すが、その UI を外したので残り続けていた。カメラ背景は元から
  Solid Color・alpha 0 で正しかった。Editor の「別シーン」は AR Foundation の XR Simulation が
  Play 中に足す `Simulated Environment Scene`（Editor 専用。Android の loader は OpenXR のみ）。
  **③指がすり抜ける**: XRI 3.5 のワールド空間 UI Toolkit の受け口が欠けていた——`XRUIToolkitManager` 無し・
  `PanelInputConfiguration`（Redirection = Never）無し・`XRUIInputModule.bypassUIToolkitEvents` が true・
  板の BoxCollider が UI px のまま（100 倍・中心が 26m ずれ）・`isTrigger` で Interactable の
  コライダー一覧から外れていた。XRI の `World Space UI` サンプルを `Assets/Samples/` に取り込み、
  見本の板と1つずつ突き合わせて直した。板の初期位置も原点から胸〜目線の高さへ。
  検算: EditMode 42件＋PlayMode 4件が緑。APK は `Build/KitchenXR_v1.0.1.apk`（bundleVersion 1.0.1 /
  versionCode 2、アイコン設定済み）。**主人が見る点**: パススルーが出るか・日本語が読めるか・
  指で「次へ／戻る」が押せてレイとピンチでは反応しないか・文字の大きさが 1m 先で読めるか。
- 2026-09-12（v1.0.2。v1.0.1 で残った「ボタンが押せない」の決着）:
  **原因は板の当たり判定の薄さだった**（受け口の欠けではない。v1.0.1 で揃えた
  `XRUIToolkitManager`・`PanelInputConfiguration(Never)`・`bypassUIToolkitEvents=false` は正しく、
  実際 Kitchen.unity をそのまま PlayMode で動かすとクリックは通っていた）。
  `XRPokeInteractor` は指が当たり判定の外に出ると掴みを手放す（`ResetPointerState`）。
  出られる余裕は「箱の半分の厚み ＋ 約16mm」しかなく、板の箱は厚み 0.02 ローカル単位＝実寸 4mm
  だったので、**板の面から 18mm 奥へ入った時点で指が抜けていた**。手応えの無いホログラムを指で押せば
  普通はもっと深く突き抜けるので、`PointerDown` は出るのに `PointerUp` が板の外で起き、
  UI Toolkit の `Button.clicked` が発火しない。実機で「触ると色は変わり振動するのに反応しない」と
  見えていたのはこれ（色と振動は `XRSimpleInteractable` のホバー＝当たり判定側の話で、UI とは別系統）。
  直し: 箱を**裏側にだけ** 1.2 ローカル単位（＝24cm）伸ばした（表の面＝押し込みの判定位置は板のまま。
  手前に張り出すと触れる前に反応してしまう）。PlayMode 試験で 22cm 突き抜けても押せることを確認。
  併せて `UIDocument.pivot` を `TopLeft` に。既定の `Center` のままだと板の矩形とコライダーが半分ずれ、
  **板の左半分（＝「戻る」）には当たり判定が無い**状態だった（XRI の World Space UI サンプルの板は
  4枚とも TopLeft）。板の組み立ては `Presentation/WorldSpacePanelFactory` に一本化し、
  シーン生成（Editor）と PlayMode 試験が同じ経路を通るようにした。
  また、チュートリアル UI を消したせいで参照を失っていた MR テンプレートの `OcclusionManager` が
  起動のたびに `UnassignedReferenceException` を投げていた（`Start()` がそこで止まるので元々何もしていない。
  手の遮蔽が効くのは `ARShaderOcclusion` の働き）ので、シーン生成時に無効化するようにした。
  検算: EditMode 44件＋PlayMode 7件が緑。APK は `Build/KitchenXR_v1.0.2.apk`
  （bundleVersion 1.0.2 / versionCode 3）。
  **主人が見る点**: 指で「次へ」「戻る」が**両方**押せるか（v1.0.1 では「戻る」は当たり判定すら無かった）・
  勢いよく突き抜けても反応するか・板に触れる前に反応してしまわないか。
  なお**レイ（コントローラー）でボタンが反応しないのは仕様**——`CookingModeInputGate` が調理モードの間
  `NearFarInteractor`／`XRRayInteractor` を無効にしている（設計 §4.4・§7 の誤操作防止）。
- 2026-09-13（v1.0.3。設計 §11 追補の実装。動画の板＝P4 は次の担当）:
  **①ポークを「触れたら反応」に**。`Button.clicked`（＝押し上げ）をやめ、`Presentation/PokePress.cs` で
  **押し下げ（PointerDown）**に反応する（押し上げは見ない）。二重発火は `ClickDebounce`（600ms）＋
  「その指が板から離れる（PointerUp／Leave／Out／捕捉解除）まで次を受けない」で止める。
  PlayMode 試験を書き換えて、面から **5mm・50mm・200mm** のどれでも1回だけ発火すること、
  **抜かずに留まったまま 600ms を越えても2回目が起きない**こと、抜いて押し直せばまた受けることを示した。
  **②進捗の点を工程の数だけ**（9工程なら9つ。phase の変わり目は点の間隔）。**③本文がボタンと重ならない**
  （ボタンの行を下に固定し、本文は ScrollView に収めて溢れたら縦スクロール。「本文を突いても進まない」試験つき）。
  **④工程の画像＋オフライン前提**: `Net/RecipeStore.cs`（`Application.persistentDataPath/recipes/<id>/`）。
  表示は常にローカルから読み、無ければその場で取って保存、取れなければ材料名の淡い札。見本（Resources）も
  初回に写して同じ経路を通す。起動時に hero と全工程を裏で先読み。EditMode 試験5件（保存と読み戻し・URL 無し・
  二重取得の抑止・先読み）はネットを差し替えて回す。**⑤材料の板**: 横スクロール禁止（`overflow: hidden`＋縦のみ）・
  文字を一段小さく（`--font-size-ingredient` 5.5px ≒ 25pt）・行の高さを揃えて板の幅に収める（溢れは「…」）。
  **⑥タイマーを常時使える**: 上の作り口で 1/3/5/10分・±30秒を選んで「開始」、動いているものを縦に3つまで積む
  （停止＝一時停止／リセット／消す）。工程に `timer_sec` があればその工程で既定値に入る。終了は
  その場で作った合成音（880Hz を3点）と板の点滅。**⑦本文の文字を1段下げた**（7px ≒32pt → 6px ≒27pt。見出しは据え置き）。
  **⑧** `bundleVersion 1.0.3` / `versionCode 4`、APK は `Build/KitchenXR_v1.0.3.apk`。
  検算: EditMode 49件＋PlayMode 11件が緑。
  **ずらした点**: (a) 材料のチェックは `Toggle` をやめて自前の行にした——UI Toolkit の Toggle は押し上げで
  値が変わるので、押し下げで反応させると浅く突いて戻したときに2回反転して元に戻る。行そのものが的になるので
  的も大きい。(b) タイマーは `TimerPanel` が `CookTimer` を直接持つ（`CookSession` の timers は使わない）——
  任意の長さのタイマーは工程に紐づかないため。Domain の一時停止は無いので、止めた瞬間の残りは Presentation が
  覚えて「再開」で作り直す（Domain は触っていない）。(c) タイマーの板は 4cm 角のボタンを6つ並べるため
  44cm×44cm に拡げた（材料は 30cm×38cm のまま）。(d) `.kitchen-button` の余白を 10px→5px にした
  （＝ボタンの間隔 4cm→2cm。設計 §7 の下限ちょうど。本文の置き場を空けるため）。
  **主人が実機で見る点**: ①指を**深く突っ込んでも**「次へ」「戻る」が効くか・**触れた瞬間**に効くか・
  留めたままで二重に進まないか。②工程の点が9つ出て1つずつ進むか。③本文がボタンに隠れないか。
  ④工程の写真が出るか（初回は通信が要る。以後は機内モードでも出るはず）。⑤材料の板に横スクロールが出ないか・
  文字が小さすぎないか。⑥タイマーを 1/3/5/10分と ±30 秒で作って3つ同時に動かせるか・鳴ったときに音と点滅が来るか。
  ⑦本文の大きさがちょうどか（1m 先で読めるか）。

- 2026-09-13（v1.0.4。**P4 動画の板**）: 主人の `Assets/TLab/Youtube/YoutubePlayer.prefab` を
  **書き換えずに包んで** 4枚目の板にした（設計 §6）。
  **①板の作り**: `Presentation/Video/VideoPanel`（UI Toolkit）が操作部を持ち、**絵は主人のプレハブの
  ワールド空間 Canvas ＋ RawImage をそのまま**「動画の窓」の 0.6mm 手前へ重ねる。
  UI Toolkit の `backgroundImage` に貼り替えなかったのは、(a) `TLabWebView` が毎フレーム
  `m_rawImage.texture` を自分で差し替える（GLES は外部テクスチャ、Vulkan は毎フレーム新しい
  `Texture2D`）経路が SDK の試されている道であること、(b) UI Toolkit の**動的アトラス**に焼かれると
  外部テクスチャの更新が届かず最初の1枚で固まる（実機でしか出ない止まり方）ため。
  ポークの当たり判定は**UI 側の板だけ**が持つ（プレハブの `GraphicRaycaster`・`Button`・
  `TLabWebViewInputField` は実行時に止める。設計 §6「文字入力は置かない」）。
  **②操作**: 一覧から選ぶ／再生・一時停止／音量 ±10／16:9 ⇄ 9:16。全部 `PokePress`（押し下げ発火）。
  **③一覧**: `Assets/StreamingAssets/media.json`（見本は YouTube 公式チャンネルの Rewind 2018/2019）を
  初回だけ `persistentDataPath/media.json` へ写し、以後はそちらだけを読む（`Net/MediaStore`）。
  最大8件・壊れた行は飛ばす・URL を貼っても id を取り出す・**JS へ埋めるので記号入りの id は通さない**
  （`Domain/MediaJson`）。書き方は `Docs/media-json.md`。
  **④Editor**: WebView は Android のプラグインなので Editor では板に「動画は実機で（Quest 3）」の札。
  一覧もボタンも動く（`IVideoPlayer` の裏が `NullVideoPlayer` に替わるだけ）。
  **⑤配置**: レシピの右上＝**タイマーの板の上** 4cm。16:9 は板 53.2×53.2cm（窓の幅が設計どおり 50cm）、
  9:16 は 31.2×59.2cm（窓 20.25×36cm）。向きを変えても**板の下辺は動かない**（下のタイマーへ食い込まない）。
  **⑥Android**: Graphics API に Vulkan＋**OpenGLES3** を併記（`TLabWebView` は一部の処理が GLES API に
  依存。README の NOTICE）、Internet permission を明示、最小 API 26 以上（今は 34）。
  OpenXR の「Force Remove Internet Permission」は**検算だけ**（`Assets/XR/Settings/` は触らない）——
  今は外れているので APK に `android.permission.INTERNET` が入っていることを確認済み。
  **⑦** `bundleVersion 1.0.4` / `versionCode 5`、APK は `Build/KitchenXR_v1.0.4.apk`（110MB）。
  検算: EditMode 69件＋PlayMode 19件が緑（`PlatformIsolationTests` に
  「WebView への参照は `Presentation/Video/` の中だけ」の規則を追加）。
  **ずらした点**: (a) 主人の SDK のこの版に **`CaptureMode` は無い**（GLES と Vulkan を
  `SystemInfo.graphicsDeviceType` で自動で選ぶ古い作り）ので「ByteBuffer から始める」は設定できなかった。
  同じ意図＝安全側に寄せるなら Graphics API の並びで **OpenGLES3 を先頭にする**のが一手だが、
  Quest 3 のパススルー（Unity OpenXR: Meta）で実績があるのは Vulkan なので、動画のために描画の土台は
  動かさず Vulkan を先頭のままにした。実機で絵が出なければここを入れ替えるのが最初の一手。
  (b) `YoutubePlayer.cs` は asmdef の無いフォルダにあり `Assembly-CSharp` に入るため、asmdef 側
  （`KitchenXR.Runtime`）からは型で参照できない。主人の側に asmdef を足すのは「書き換えない」に反するので、
  `YoutubePlayerBridge` が**型の名前で引き当てて呼ぶ**（`Load`/`Play`/`Pause`/`SetVolume` の4つだけ。
  `TLabWebView` は asmdef を持つので型でそのまま呼んでいる）。
  (c) 9:16 の窓を「幅 50cm のまま」にすると高さ 89cm になって台所に置けないので、
  9:16 は**高さ 36cm** を基準に取った。
  (d) `youtube.html` の器は `padding-bottom: 56.25%` 固定なので、9:16 では
  `EvaluateJS` で style を上書きして頼む（html は書き換えない）。
  (e) Editor が `ProjectSettings.asset` の Android の define に `SENTIS_ANALYTICS_ENABLED` を
  勝手に足した（Standalone には元からある。無害なので戻していない）。`Assets/XR/Settings/` の
  fileID の入れ替えは revert 済み。
  **主人が実機で見る点**: ①板の一覧から選ぶと動画が出るか（**絵が出ないときは Graphics API の
  Vulkan と OpenGLES3 の順を入れ替える**）。②絵が板の窓にぴったり収まるか・上下に黒い余白が残らないか。
  ③再生／一時停止・音量 ±・9:16 の切り替えが指で押せるか。④9:16 にしたときショーツが縦いっぱいに出るか。
  ⑤動画の窓を触っても誤ってボタンが反応しないか。⑥音が調理中に聞こえる大きさか（既定 70）。
  ⑦レシピの右上（タイマーの上）という置き場が見やすいか——高すぎれば P2 のアンカーで動かせる。

- 2026-09-13（v1.0.5。**P3 レシピ帳と結ぶ**＋ P5 の復帰）: manor の料理長のレシピ帳
  （ADR-015 D3）を**読む側**として繋いだ。manor には何も足していない（依存は一方向のまま）。
  **①繋ぎ方は板ではなくファイル**: `persistentDataPath/manor.json`（`{"base_url", "passcode"}`）。
  無ければ一覧に「manor 未設定（見本だけ）」の札が出て、見本の炒飯だけが並ぶ（アプリは動く）。
  書き方は `Docs/manor-connection.md`（`adb push` の手順・`tailscale serve` と `manor web serve --host`・
  札の読み方）。設計 §6「文字入力はパネルに置かない」に従い、Quest 側に入力欄は1つも無い。
  **②`Net/ManorClient`**: `UnityWebRequest` を `IHttpTransport` 越しに呼ぶ（試験では差し替え）。
  **cookie は自分で持つ**——`Set-Cookie` から `manor_session` を取り出し、以後 `Cookie:` 見出しを
  手で付ける（Android の自動 cookie の生き死にはこちらから見えないので頼らない）。
  **401 なら1度だけ入り直して同じ頼みを送り直す**（2度目の 401 で諦める。manor 側の 429 を誘わない）。
  loopback の manor は cookie を返さないが、それも成功として扱う。
  口は `ListRecipes`／`GetRecipe`／`StartSession`／`PostEvent`／`CurrentSession`／`EndSession` の6つ。
  例外は投げず `ManorResult<T>`（取れた／繋がらない／断られた）で返す。
  **③オフライン前提の徹底**: 一覧は取れたら `recipes/index.json` へ**そのまま写し**、
  読むのは常にその写し。レシピを選ぶと契約 JSON と画像（hero＋全工程）を**先に全部**手元へ
  落としてから調理を始める（その間は一覧に「準備中 n/m」の覆いが出て、別の行を受け付けない）。
  進行（`next`/`prev`/`end`）は追記ファイルの待ち行列 `cook_events.jsonl` に積み、繋がったときに
  **積んだ順に**送って送れた分だけ消す（起動時にも流す）。**送れないことで調理は止まらない。**
  **④`Presentation/RecipeListPanel`**: 起動時はレシピの板の場所にこれが出る。題名・分・分類・kcal の
  行（最大 20 件・縦スクロール）で、行そのものが的（`PokePress` の押し下げ発火。高さ 20px＝4cm）。
  先頭は必ず「見本: 炒飯」。**⑤「一覧へ」は2度押し**（レシピの板の頭。1度目で「もう一度」に変わり
  4秒で戻る）。長押しにしなかったのは、押し下げ発火と長押しの判定が噛み合わないため——
  手応えの無い板は深く入って留まるのが普通で、「長押し」が1回押しと区別できない。
  **⑥途中起動の復帰（P5 前倒し）**: 起動時に `cook-sessions/current` を見て未終了があればその工程から
  再開（一覧を飛ばす）。繋がらなければ `last_session.json` から戻す。
  **⑦終了**: 最後の工程を越えると「作り終えた」が出て、`end` を待ち行列へ積んで一覧へ戻る。
  **⑧** `bundleVersion 1.0.5` / `versionCode 6`、APK は `Build/KitchenXR_v1.0.5.apk`（110MB）。
  検算: EditMode 93件＋PlayMode 26件が緑（EditMode +24・PlayMode +7）。
  **ずらした点**: (a) 板の出し入れは `GameObject.SetActive` ではなく
  `Presentation/PanelVisibility`（root の display＋BoxCollider＋XRSimpleInteractable を一緒に切る）
  ——`UIDocument` は無効化のたびに `rootVisualElement` を作り直すので、各パネルが `Awake` で
  掴んだ要素の参照が死ぬ。(b) 一覧が出ている間は**タイマーの板も引っ込める**（主人の指示
  「調理の3枚に切り替え」に従った。§11 追補⑥「タイマーは常時使える」は調理中の話と読んだ）。
  (c) manor の `start_session` は「未終了があればレシピを問わずそれを返す」ので、
  別のレシピの途中が残っていると進行がそちらに記録されてしまう。**選ぶ前に `current` を見て、
  別のレシピの途中なら `end` してから始める**ようにした——手放した調理が `times_cooked` に
  1つ数えられるのは承知の上（manor に「やめる」の口が無い。違うレシピの工程を別の帳簿へ
  書き込むほうが悪い）。(d) 「一覧へ」では manor のセッションを**終わらせない**（次の起動で
  そこから復帰するのが設計 §5）。(e) 待ち行列の 4xx は**その1件だけ捨てて先へ進む**
  （既に終わったセッションへの `next` 等を残すと行列が永久に詰まる）。5xx と「繋がらない」は残す。
  (f) 見本（Resources の炒飯）の id は文字列なので manor のセッションは作らない——
  進行は `last_session.json` にだけ残る。
  **主人が実機で見る点**: ①起動してレシピを選ぶ板が出るか・「manor 未設定（見本だけ）」の札が
  出るか（`manor.json` を置く前）。②`manor.json` を置いて起動し直すと manor の一覧が並ぶか
  （題名・分・分類・kcal が読めるか・行が指で押せるか）。③選んでから「準備中 n/m」が出て、
  終わると調理の板3枚に切り替わるか。④**機内モードにしてから**次へ／戻るが止まらずに動くか・
  工程の写真が出るか。⑤機内モードを解いて少し待つと manor 側の `current` が追いつくか
  （manor の Web で確認）。⑥途中でアプリを落として起動し直すと同じ工程から続くか。
  ⑦最後まで進めて「作り終えた」を押すと一覧へ戻り、manor の `times_cooked` が増えるか。
  ⑧「一覧へ」が1度押しでは戻らず2度押しで戻るか（誤操作防止）。

- 2026-09-13（v1.0.6。**P2 アンカーと配置モード**）: 板の置き場所を覚えるようにした（設計 §0 A
  「空間に定着した UI は土台」・§4.3・§4.4）。
  **①`Platform/ArFoundation/ArAnchorStore`**: AR Foundation 6.5 の永続アンカー。
  保存は `TryAddAnchorAsync(世界の Pose)` → `TrySaveAnchorAsync` の `SerializableGuid` を
  `persistentDataPath/anchors.json`（`{鍵: GUID}`）へ。復元は `TryLoadAnchorAsync` で返った
  アンカーの Transform。同じ鍵を保存し直すときは先に `TryEraseAnchorAsync`。
  **鍵は板1枚＝1つ**（`panel.recipe`／`ingredients`／`timer`／`video`。一覧はレシピと同じ鍵）。
  文書で確かめたこと: Meta Quest（Unity OpenXR: Meta 2.5）は Save／Load／Erase に対応するが
  **Get Saved Anchor Ids は非対応**（＝GUID は自分で持つほか無い）。**Editor の XR Simulation は
  Save／Load／Erase のどれも非対応**なので、エディタでは必ず控えの側を通る。
  **②退避路（`panels.json`）**: 板の位置を **XR Origin 基準の相対 Pose** で持つ。
  アンカーが使えるときも**必ず一緒に書く**——アンカーの復元は部屋が変わった等で普通に失敗するので、
  その受け皿が要る。壊れたファイル・数の欠けた行・長さ 0 の回転は「無かった」として既定へ落ちる。
  **③配置モード**: 入り方は**手のひらメニュー**（XRI の `HandMenu` を MR テンプレートの
  `Left/Right Hand > Palm` と Hands Interaction Demo の追従設定に結んだ）と、
  レシピ／一覧の板の頭の**「配置」2度押し**の2通り。最中は全ての板に琥珀の枠と取っ手が出て、
  **Ray ＋ Grab**（`XRGrabInteractable`・kinematic な Rigidbody・掴み口は触れたところ）で動かせる。
  操作は「保存」「元に戻す」「やめる」の板から。
  **④起動時の復元順**: アンカー → 控え → 既定（`Bootstrap` が頭の前へ配った位置）。
  戻した板は 0.6 秒かけて今の位置から目的地へ補間しながら薄く出す。
  **⑤** `bundleVersion 1.0.6` / `versionCode 7`、APK は `Build/KitchenXR_v1.0.6.apk`。
  検算: EditMode 108件＋PlayMode 34件が緑（EditMode +15・PlayMode +8）。
  `PlatformIsolationTests` の例外を `Platform/` 全体から **`Platform/<系>/` だけ**へ狭めた
  （設計 §4.2 の文言どおり。`Platform/PanelPoseFile.cs` や `Platform/Null/` に AR Foundation が
  混ざらないように）。
  **ずらした点**: (a) 「配置モードでは Poke のボタンが効かない」を、**ポークの Interactor を切る**のではなく
  **調理の板の UI に板ガラスを1枚かぶせる**（`CookingModeInputGate`）で実現した——Interactor ごと切ると
  「保存」の板も手のひらメニューも指で押せなくなり、実機でレイが UI Toolkit に届かなかったときに
  配置モードから出られなくなる。狙い（誤って工程が進まない）は板ガラスで満たせる。
  (b) `Bootstrap` の分かれ道は「シーンに `ARAnchorManager` が居るか」だけを見る
  （`AnchorStoreFactory`）。**保存に対応しているかは起動直後には分からない**（AR Session が立つまで
  descriptor が無い）ので、そこは呼ばれるたびに見て false／null を返し、控えへ落ちる。
  結果として Editor では「アンカーは常に失敗 → 控えだけが残る」となり、指示の
  「Editor は InMemory ＋退避路」と同じ振る舞いになる。
  (c) 既定の置き場所（頭の前 0.8m）は**変えていない**——今の `Bootstrap` の配置をそのまま既定とした。
  (d) レシピの板と一覧の板は同じ鍵なので、配置モードでは**レシピの板だけ**を出して掴ませ、
  出るときに一覧をそこへ揃える（同じ場所に2枚重なった状態で掴ませない）。
  **主人が実機で見る点**: ①手のひらを返すと「配置」の小さな板が出るか（出なければレシピ／一覧の板の
  頭の「配置」を2度押し。**こちらは必ず動く**）。②配置モードで板に枠と取っ手が出るか・
  **レイで遠くから引き寄せて**置き直せるか・**指で「次へ」を突いても工程が進まない**か。
  ③「保存」を押すと調理モードに戻るか。④**アプリを落として起動し直すと同じ場所に戻るか**
  （3日連続で確認——これが P2 の済みの印）。⑤戻るときに板がゆっくり出るか。
  ⑥「元に戻す」で入る前の位置へ戻るか。⑦配置の操作板（保存・元に戻す・やめる）が
  **レイでも指でも**押せるか——レイで押せないときは指で押して抜けられる。
  ⑧一度部屋を出て戻ってもアンカーが効くか（効かなければ控えの位置に出る＝原点のずれ分だけずれる）。

## 板を置き直す（配置モードの使い方）

1. **入る**: 手のひらを自分に向けると小さな板が出るので「配置」を押す。出ないときは、レシピの板
   （または起動直後の一覧の板）の頭にある「配置」を**2度**押す（1度目で「もう一度」に変わり、4秒で戻る）。
2. **動かす**: 全ての板に琥珀の枠と取っ手が出る。**コントローラのレイ**で板を指して掴むと、
   遠くからでも引き寄せて置き直せる。配置モードの間は、調理の板のボタン（次へ・戻る等）は**効かない**。
3. **決める**: 目の前に出る操作板の「保存」で覚えて調理モードへ戻る。「元に戻す」は入る前の位置へ、
   「やめる」は戻して抜ける。
4. **次の起動**: 覚えた場所に板がゆっくり戻る。アンカーが効かなかったときは控え（部屋の原点基準）の
   位置に出るので、少しずれることがある——そのときはもう一度置き直して「保存」すればよい。
   覚えを捨てたいときは `Application.persistentDataPath` の `anchors.json`・`panels.json` を消す
   （`adb shell run-as com.kazuhiro.kitchenxr` か、アプリのデータ消去）。

## エディタでの Play について（2026-09-12）
- **Play すると別のシーンが増えるのは正常**。XR Plug-in Management の **Standalone** の loader が
  AR Foundation の **XR Simulation**（`SimulationLoader`）なので、Play 中に模擬環境のシーンが
  additive で足される。ヘッドセット無しで AR Foundation を動かすのに要るので消さないこと。
  「別のシーン」に見えないよう、環境は最小の空プレハブ
  `Assets/XR/UserSimulationSettings/MinimalSimulationEnvironment.prefab` に差し替えてある
  （既定のままだと床・壁・机のある部屋が出る）。
  **Quest Link で実機の映像を見ながら Play したいときは、Project Settings > XR Plug-in Management の
  Standalone タブで Plug-in Provider を `OpenXR` に切り替える**（Android タブは触らない。実機ビルドは
  そちらを使う）。確かめ終わったら XR Simulation に戻すと、ヘッドセットを繋がずに Play できる。
- **Main Camera の回転が Play 中に勝手に変わるのは仕様**。`Main Camera` には `TrackedPoseDriver` が付いていて、
  XR 機器（または XR Simulation の模擬 HMD）の姿勢を毎フレーム書き込む。Play 中に Inspector で直しても
  次のフレームで上書きされ、Play を抜けると Play 前の値に戻る。
  **Edit モードでは戻らない**ことは確認済み（シーンの複製で回転を変えて保存し開き直すと保持された。
  `[ExecuteAlways]` で camera の transform を書くコンポーネントはプロジェクトに無い）。
  つまり直すなら Play を抜けてから。
- **エディタのレイアウトが初期に戻る**のは、CLI／バッチで Unity を起動すると終了時に
  `UserSettings/Layouts/*.dwlt` が書き出されるため。AI／CI が Editor を起動する前後で
  `Tools/editor-layout-backup.ps1 -Backup` ／ `-Restore` を必ず呼ぶこと（`-Status` で中身を確認できる）。
