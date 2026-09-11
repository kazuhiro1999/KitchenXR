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
