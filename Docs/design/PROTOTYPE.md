# kitchen-xr — プロトタイプ設計（v0）

日付: 2026-09-11 ／ 状態: 採択（同日、主人の答えで改訂）／ 書いた人: 執事（Fable）／ 発端: 主人と ChatGPT の壁打ち

> **Unity クライアントは manor の管理下に置かない**（プロトタイプ段階）。**サーバ側は AI Manor
> そのもの**——レシピ帳は料理長の Web（manor の ADR-015）であり、XR はその API を**読む側**。
> 依存は一方向（kitchen-xr → manor）。manor が XR を知ることはなく、XR 専用の口も作らない
> （主人「密結合は避けたい」「合言葉は AI Manor に既にある」。2026-09-11 に改訂）。
>
> 置き場: `C:/Users/machi/Documents/UnityProjects/KitchenXR/`（Unity プロジェクト）。`Assets/` と
> 同じ階層に `Docs/`（この文書と ROADMAP）。

## 0. 本質の再構成（ChatGPT の要約から抜け落ちていたもの）

主人の言葉を並べ直すと、作りたいのは「XR レシピアプリ」ではなく、次の3本柱を持つ
**キッチンに常駐する料理長**である。

| 柱 | 主人の言葉 | 設計上の意味 |
|---|---|---|
| **A. 空間に定着した UI** | 「空間を覚えていることは、毎日使う上で快適にするためには必須」 | アンカーは後付けの機能ではなく**土台**。毎回の起動で同じ場所に同じパネルが戻ることが体験の核 |
| **B. 独自テンプレートのレシピ** | 「レシピサイトから情報をとって AI エージェントにテンプレートに置き換えさせ、独自の形式で表示」「文字は極力少なく、画像を適切に配置」「今と次」「進捗%」 | レシピは**サーバ側で一度構造化**し、クライアントは表示に徹する。テンプレートの JSON がこの計画で最も長生きする成果物 |
| **C. ながら見の娯楽** | 「ショーツとかドラマを見ながら料理したい。レシピを YouTube で見たいわけではない」 | 動画は**レシピと無関係**な独立パネル。主人の WebView SDK で出す（§6） |

差別化点（カメラ認識・自動進行）は**後段**。主人自身が「まずは認識＋自動進行は行わず、固定 UI
パネルをタッチしてマニュアル進行」と切っている。ただし後段が入っても壊れないよう、**認識器と
進行器の分離**と**進行の状態機械**だけは最初から形を持たせる（§5）。

横断する原則（主人の要求から）:

1. **Quest 3 依存は薄く。** PICO・WebXR へ差し替えられるよう、機種固有の呼び出しは
   アダプタに閉じ込める（§4）
2. **料理中の手は UI 操作の意図ではない。** ピンチ＝クリックの単純設計にしない（§7）
3. **AI 駆動開発で作る。** 層を薄く・契約を文書に・試験を持つ（Unity 側も EditMode 試験を置く）
4. **製品ではなく家庭用の実験。** 有料アセットは入れない（動画は主人の WebView SDK。§6）

## 1. プロトタイプの範囲（v0 で作る／作らない）

作る:

- Quest 3 上の MR（パススルー）で、キッチンに **4 枚のパネル**を置く: レシピ／材料／タイマー／動画
- **配置モード**（パネルを掴んで置く）と**調理モード**（触るだけ）の2モード
- パネルの位置は**アンカーに保存**し、次回起動で復元
- レシピは manor のレシピ帳から取得（登録は manor の Web。URL を貼る → 料理長が構造化 → 主人が直して登録）
- **手動進行**（「次へ」「戻る」）。今の工程＋次の工程・進捗%・工程の画像
- タイマー（工程に紐づく。複数同時）
- 動画パネル: 主人の WebView SDK で YouTube・ショーツを出す（§6）

作らない（v1 以降）:

- 音声操作・料理長との会話（v1）
- カメラ認識・自動進行（v2）
- WebXR 版・PICO 版（アダプタの口だけ v0 で切る）
- 音声・認識以外で manor に要求を出すこと（レシピ帳の口は manor の ADR-015 で決まる）

## 2. 全体構成

```
Quest 3 ── Unity クライアント（kitchen-xr-client）
            ├─ Presentation   パネル（uGUI World Canvas）・配置モード・調理モード
            ├─ Domain         CookSession（工程の状態機械）・Timer・RecipeModel
            ├─ Platform       IAnchorStore / IPassthrough / IHandInput（アダプタ）
            └─ Net            REST クライアント（Newtonsoft.Json・UniTask）
                    │  HTTP（LAN。Tailscale 越しも可）
                    ▼
自宅 PC ── AI Manor（`manor web serve`。既存の passcode/cookie で守られている）
            └─ 料理長のレシピ帳（ADR-015）
                 ├─ /api/v1/kitchen/recipes        一覧・取得・登録・編集・取り込み（URL → claude -p で構造化 → 下書き）
                 ├─ /api/v1/kitchen/cook-sessions   調理セッション（開始・工程イベント・終了）
                 └─ Web の別ページ /kitchen/recipes  主人が Chrome で登録・編集する画面
```

**サーバは manor**。レシピ帳は XR のためだけの機能ではなく、料理長の Web（自分専用のレシピサイト）
として manor の中に作る（主人 2026-09-11）。XR はその口を読むだけで、XR のために manor に
何かを要求しない。動画の URL 一覧（`/media`）は manor に置かず、**クライアントのローカル設定**
（`StreamingAssets/media.json`）で持つ。

## 3. レシピの契約（テンプレート JSON）

この計画で**最も長生きするもの**。クライアントもサーバも料理長も、これだけを見る。

```jsonc
{
  "id": "R12",
  "title": "鶏むね肉の照り焼き",
  "source_url": "https://…",            // 出典。表示は「出典」リンクのみ
  "hero_image": "https://…/hero.jpg",   // 任意
  "servings": 2,
  "total_minutes": 25,
  "ingredients": [
    {"name": "鶏むね肉", "qty": "1", "unit": "枚", "prep": "そぎ切り", "group": "主材料"},
    {"name": "醤油",     "qty": "大さじ2", "unit": "", "prep": "", "group": "たれ"}
  ],
  "tools": ["フライパン", "ボウル"],
  "phases": [                            // 進捗%の分母。工程を束ねる
    {"id": "prep", "title": "下ごしらえ"},
    {"id": "cook", "title": "焼く"},
    {"id": "finish", "title": "仕上げ"}
  ],
  "steps": [
    {
      "index": 1, "phase": "prep",
      "title": "肉を切る",                        // ≤ 12 文字。パネルの見出し
      "instruction": "そぎ切りにして塩こしょう。", // ≤ 60 文字。1〜2 文
      "image": "https://…/step1.jpg",            // 任意。無ければ材料の絵
      "ingredients_used": ["鶏むね肉"],
      "timer_sec": null,                          // あればタイマーの既定値
      "completion": "manual",                     // manual | auto | confirm（§5）
      "tips": ["厚さを揃えると火の通りが均一"]     // 折りたたみ。既定は隠す
    }
  ]
}
```

決めごと:

- **短さはサーバで保証する**（`title` ≤ 12・`instruction` ≤ 60 は構造化のプロンプトと検算で守る）。
  クライアントで切り詰めない——「文字は極力少なく」は表示の都合ではなく内容の要件
- `completion` は v0 では全部 `manual`。v2 で `auto`（鍋に入れた・タイマー終了）と
  `confirm`（きつね色・味見）を使い分ける。**列だけ最初から持つ**
- 画像は出典のものを URL で持つ（保存しない。個人利用の範囲）。無い工程は材料のアイコンで代える
- 分量の単位は自由文字列（「大さじ2」）。正規化は料理長の仕事になったときに考える
- **見本**: `Docs/samples/chahan.recipe.json`（主人がよく作る炒飯。出典 Nadia。manor の ADR-015 の見本も兼ねる）。出典の6工程を
  「1動作1工程」で9工程に割り、ポイント欄を該当工程の `tips` に配った。P1 のローカル JSON と
  P3 の取り込みの期待値を兼ねる。出典には工程ごとの写真があるが JSON-LD は無い（取り込みは
  HTML → Claude の構造化で行う前提）

## 4. Unity クライアント

### 4.1 土台

| 何 | 選択 | 理由 |
|---|---|---|
| Unity | **6.3 LTS（6000.3 系）** | ワールド空間の UI Toolkit（6.2 以降）・XRI 3.x・AR Foundation 6・Sentis が揃う。主人の裁定 2026-09-11「初期なので 6.3 も可」 |
| XR | **OpenXR Plugin ＋ XR Interaction Toolkit 3.x** | 主人の慣れ。機種非依存の入力（Hands・Poke・Grab） |
| MR（パススルー・アンカー） | **AR Foundation 6 ＋ Unity OpenXR: Meta**（`com.unity.xr.meta-openxr`） | Unity 公式の OpenXR 経路でパススルー・平面・**永続アンカー**が取れる。Meta XR Core SDK を入れない＝Quest 依存が薄い。PICO も OpenXR の AR Foundation 対応で同じ口に乗る見込み |
| UI | **UI Toolkit（ワールド空間）＋ Noto Sans JP の Font Asset** | UXML/USS のテキストで見た目を書ける（AI が書いて差分を読める・角丸や影をスプライト無しで）。XRI 3.2 以降がレイとポーク（Button/Toggle）に対応。**P1 で実機の触り心地を確かめ、不具合が多ければ uGUI へ戻す**（パネルは薄い層なので損失は限られる）。動画パネルは WebView のテクスチャを板に貼るので UI Toolkit と独立 |
| 非同期 | UniTask | `async` を Unity の寿命に合わせる |
| JSON | Newtonsoft.Json（`com.unity.nuget.newtonsoft-json`） | 契約 JSON の直列化 |
| 演出 | **USS の transition**（UI Toolkit）。3D の補間は数行で自前 | DOTween は Transform 向けで UI Toolkit の要素には効かない。要るようになったら UPM で入る LitMotion（2026-09-12 主人の問いに答えて確定） |
| 試験 | Unity Test Framework（EditMode）で Domain 層を試験 | AI 駆動開発の関門 |

無料アセット候補（入れるなら主人へ）: XRI **Starter Assets**・**World Space UI** サンプル（UI Toolkit の板の見本）、
Google **Material Symbols**（Font Asset にしてアイコンを文字として出す）、**Noto Sans JP**（フォント）。
見た目の規約（色・角丸・文字の3段）は USS の変数（`--color-accent` 等）として1ファイルに置く。
参考: [unity-ui-toolkit-design-system](https://github.com/sinanata/unity-ui-toolkit-design-system)。

### 4.2 層

```
Assets/
  KitchenXR/
    Domain/        CookSession.cs（工程の状態機械）  RecipeModel.cs  TimerModel.cs  ← Unity 非依存。EditMode 試験
    Platform/      IAnchorStore.cs  IPassthroughControl.cs  IHandInputPolicy.cs
      ArFoundation/ ArAnchorStore.cs（ARAnchorManager の保存・復元）  ArPassthrough.cs（ARCameraManager）
      Null/        InMemoryAnchorStore.cs（Editor・アンカー非対応機で使う）
    Net/           KitchenApi.cs（REST）  Dto/…
    Presentation/  UI/（*.uxml・*.uss・theme.uss）  RecipePanel.cs  IngredientsPanel.cs  TimerPanel.cs  VideoPanel.cs  PlacementMode  CookingMode
    App/           Bootstrap.cs（DI。どのアダプタを挿すかはここだけ）
```

- **Domain は MonoBehaviour を持たない。** `CookSession` は「工程・進捗・タイマー」を純粋な
  C# で持ち、Presentation はそれを映すだけ。v2 の認識イベントも Domain の `Apply(event)` に
  入るので、認識器と進行器の分離が構造で守られる
- **機種・SDK 固有の呼び出しは `Platform/<系>/` にしか書かない**（検算: `UnityEngine.XR.ARFoundation`・
  `OVR`・`PXR` を grep して `Platform/` の外に出たら落ちる EditMode 試験）

### 4.3 アンカー（土台）

```csharp
public interface IAnchorStore {
    UniTask<bool> SaveAsync(string key, Pose pose);     // key = "panel.recipe" など
    UniTask<Pose?> LoadAsync(string key);
    UniTask ClearAsync();
}
```

- Quest: `ArAnchorStore`（AR Foundation 6 の `ARAnchorManager.TrySaveAnchorAsync` / `TryLoadAnchorAsync`。
  Unity OpenXR: Meta が永続化を提供する。`SerializableGuid` を JSON に保存し、起動時に読み戻す）
- 対応機が無いとき: `InMemoryAnchorStore` ＋ **手動キャリブレーション**（「台の角」を1点
  指して、その相対で復元）。PICO・WebXR はそれぞれのアンカー API のアダプタを後で足す
- **保存の単位はパネル1枚＝鍵1つ。** 「台所全体の座標系」を作らない（アンカーは近くほど
  精度が高い。パネルごとに置くほうが素直）

### 4.4 2つのモード

| | 配置モード | 調理モード（既定） |
|---|---|---|
| 入り方 | 手のひらメニュー、または「配置」ボタン長押し | 起動時・配置を「保存」した後 |
| 手の入力 | Ray ＋ Grab（掴んで動かす・遠くから引き寄せる） | **Poke（触る）だけ。Ray とピンチは無効** |
| 画面 | パネルに枠と取っ手が出る | 枠なし |
| 出方 | 「保存」→ アンカーに保存 | — |

## 5. 進行の状態機械（v0 は手動。v2 の口を持つ）

```
CookSession
  recipe: Recipe          current: int           timers: Timer[]
  Apply(SessionEvent)     // NextRequested / PrevRequested / StepDone(index) / TimerStarted / TimerElapsed
                          // v2: Observation(step, evidence, confidence)  ← 認識器はこれだけ送る
  Progress => 済んだ工程 / 全工程（phase 単位で丸める）
```

- v0: `NextRequested`／`PrevRequested` だけ。**戻るは常に可能**（誤タッチの取り消し）
- v2 の設計は今から決めておく（ChatGPT の要約が正しく捉えていた部分）:
  `Observation → Candidate → Stable → Completed` の段階を Domain に持ち、`completion: auto` の
  工程だけ Stable → Completed を自動にする。`confirm` の工程は「済んだようです」の**提案**に
  留める。`manual` は何もしない。**認識器は工程を進めない。観測を送るだけ**
- 途中起動の復帰: セッションをサーバに持たせ（§8 `/sessions`）、再起動時に `current` を戻す

## 6. 動画パネル（主人の WebView SDK を使う）

主人が過去に [TLabWebViewVR](https://github.com/TLabAltoh/TLabWebViewVR) を基に **Android で動く
WebView の SDK（unitypackage）**を作っている。動画パネルはこれを使う——YouTube・ショーツ・
一般の Web 動画がそのまま出る。

- v0: 動画パネル＝WebView 1枚。操作は「URL を開く／再生・停止／音量」だけ（主人が「キーボード
  入力周りは若干使いにくかった」と言っているので、**文字入力はパネルに置かない**。開く URL は
  サーバの `GET /media` の一覧から選ぶ。一覧は PC 側で書く）
- 16:9 と 9:16（ショーツ）の切り替え
- 線引きは1つだけ残る: **DRM 付きの配信（Netflix・Prime 等）は WebView では再生できない**
  （Widevine L1 が無い）。これはプラットフォームの制約であり、設計では越えられない
- SDK の導入は P0 の最後（プロジェクトができてから主人が unitypackage を入れる）

## 7. 誤操作を防ぐ（料理中の手）

- 調理モードでは **Poke だけ**（§4.4）。Ray・ピンチ・ジェスチャは配置モードにしか無い
- ボタンは**大きく疎に**（最小 4 cm 角・間隔 2 cm 以上）。押せるのは「次へ／戻る／タイマー
  開始・停止／材料チェック」の4種類だけ
- **hysteresis**: 押し込み深さで「入り」と「抜け」の閾値を分け、指が震えても連打にならない
- **同じボタンの連打を 600 ms 抑える**（「次へ」を2回押した＝誤操作、と見なす）
- パネルは**手の高さより上**（胸〜目線）に置くのを既定にし、作業中の手が通る高さに置かない
  （配置モードで警告を出す）
- 取り消しは常に1タップ（「戻る」）。破壊的な操作（セッション終了）だけ長押し

## 8. サーバの契約（AI Manor の料理長。正は manor の ADR-015）

契約の正は `manor/docs/design/ADR-015_recipe_book.md`。XR クライアントが使う口だけ抜粋する。

| 口 | 何 |
|---|---|
| `POST /api/v1/auth/login {passcode}` | 既存。返る cookie を保持して以後に付ける（ループバックなら不要）。寿命 24 時間 |
| `GET /api/v1/kitchen/recipes` | 一覧（title・hero・total_minutes・tags・favorite・times_cooked） |
| `GET /api/v1/kitchen/recipes/{id}` | 契約 JSON（§3）＋ `meta`（栄養・タグ・評価・メモ） |
| `POST /api/v1/kitchen/cook-sessions {recipe_id}` | 調理開始。返り `{id, current}` |
| `POST /api/v1/kitchen/cook-sessions/{id}/events {type, step?}` | `next` / `prev` / `timer_start` / `done`。返り `{current, progress}` |
| `GET /api/v1/kitchen/cook-sessions/current` | 途中起動の復帰 |
| `POST /api/v1/kitchen/cook-sessions/{id}/end` | 終了（`times_cooked` が増える） |

登録・取り込み・編集は manor の Web（`/kitchen/recipes`）で行い、XR からは行わない。
取り込みの構造化（`claude -p`・文字数の上限・1動作1工程）も manor 側の仕事（ADR-015 D2）。

## 9. 画面の設計（モダンに）

- 配色: **明るい**半透明の白い板（`#FFFFFF` 85%）に濃灰の文字（`#1F2937`）。強調色は1色（琥珀 `#F59E0B`）。
  角丸は大きめ・影は薄く。Unity 既定の見た目は使わない（**`theme.uss` を最初に作り、全パネルがそれを継ぐ**）
- レシピパネル（主）: 上に phase の点列と進捗%、中央に**今の工程**（見出し・画像・1〜2文）、
  下に**次の工程**（見出しだけ・薄く）、右下に「次へ」、左下に「戻る」
- 材料パネル: チェックリスト。今の工程で使う材料を強調
- タイマーパネル: 大きな数字。工程に `timer_sec` があれば「開始」を出す。複数を縦に積む
- 動画パネル: 16:9。再生／停止／音量だけ。ショーツ用に 9:16 へ切り替え
- 文字: 3段階（見出し 48pt・本文 32pt・補助 24pt）。1 m 先で読める大きさを Editor で検算

## 10. 裁定（2026-09-11 主人）

1. Unity **6.3 LTS**（同日改訂。最初は 6000.0.70f1 の予定だったが、UI Toolkit をワールド空間で使うため）。Hub の **Mixed Reality テンプレート**で作る（AR Foundation・
   XRI 3・OpenXR・Meta OpenXR が最初から揃う。VR テンプレートはパススルー無しなので使わない）
2. 動画は主人の WebView SDK（§6）。有料アセットは不要
3. サーバは manor と同じ PC。**manor は API を提供するだけで kitchen-xr に依存しない**
4. 置き場は `C:/Users/machi/Documents/UnityProjects/KitchenXR/`。`Docs/`・`Server/` を `Assets/` と
   同じ階層に置く
5. Unity 公式の **Claude Code プラグイン**（Unity の skill・Unity CLI・Editor を動かす MCP）を
   このプロジェクトで試す
