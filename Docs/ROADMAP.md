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
| **P4 動画** | 動画パネル（主人の WebView SDK）。`GET /media` の一覧から開く。16:9／9:16 の切り替え。文字入力は置かない | 料理しながらショーツが流れる | 1週 |
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
