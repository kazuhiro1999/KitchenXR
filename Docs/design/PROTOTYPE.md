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
| 入り方 | 手のひらメニューの「配置」、またはレシピ／一覧の板の「配置」（2度押し） | 起動時・配置を「保存」した後 |
| 手の入力 | Ray ＋ Grab（掴んで動かす・遠くから引き寄せる） | Poke（触る）＋ **動画の板にだけ Ray** |
| 画面 | **見えている板**に枠と取っ手が出る（重なった裏の板は引っ込める） | 枠なし |
| 操作の置き場 | **手のひらメニュー**（保存・元に戻す・板を手元に・やめる） | 手のひらメニューは「配置」1つ |
| 出方 | 「保存」→ アンカーと控えへ／「やめる」→ 元に戻す | — |

（2026-09-13 v1.0.7 の実機確認で作り直した。理由と仕組みは §11 の「配置とレイ」を見よ。）

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

- 調理モードでは **Poke だけ**（§4.4）。ただし**動画の板だけは Ray も効く**
  （主人「Youtube は少し離れた場所に置くので遠隔から操作したい」。2026-09-13。仕組みは §11「配置とレイ」）。
  ほかの板は Ray の当たり判定そのものを外すので、レイを向けて摘まんでも「次へ」は押せない
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

**繋ぎ先と合言葉はファイルで渡す**（2026-09-13・P3）。`Application.persistentDataPath/manor.json`
（`{"base_url": "…", "passcode": "…"}`）。板に文字入力を置かない（§6）ため、主人は PC から
`adb push` する。書き方と確かめ方は [`Docs/manor-connection.md`](../manor-connection.md)。
置かれていなければ一覧に「manor 未設定（見本だけ）」の札が出て、見本の炒飯だけが並ぶ。
cookie（`manor_session`）はクライアントが自分で持ち、401 が返ったら**1度だけ**入り直して送り直す。

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

## 11. 追補（2026-09-13 主人の実機確認 v1.0.2 から）

- **ポークは「触れたら反応」に。** v1.0.2 は表面に触れてすぐ戻さないと反応せず、押し込みすぎると
  反応しない（Button の click＝押し上げが板の中で起きる必要があるため）。調理中の手は深く入る
  のが普通なので、**押し下げ（PointerDown）で発火**し、押し上げは見ない。連打の抑止（600 ms）と
  「一度板から離れるまで次を受けない」履歴で二重発火を防ぐ
- **進捗は工程の数だけ点を出す**（phase の3つではなく）。次へで1つ進む。phase の区切りは点の間隔か色で
- **本文がボタンと重ならない**（ボタンの行を下に確保し、本文は残りに収める。溢れたら縦スクロール）
- **工程の画像を出す**。あわせて**オフライン前提**: 電子レンジ使用時などに通信が切れる。レシピの
  JSON と画像（hero・工程）は**取得したときにローカルへ保存**（`Application.persistentDataPath/recipes/<id>/`）
  し、表示は常にローカルから。無ければその場で取りに行き、取れなければ札で代える。見本（Resources）
  も同じ経路を通す
- **材料の板**: 横スクロールを出さない（縦のみ）。文字を一段小さく（本文 28pt 相当以下）、板の幅と
  行の高さを合わせる。今の工程で使う材料の強調はそのまま
- **タイマーは常時使える**: 任意の時間（1/3/5/10 分の押し釦と ±30 秒）・開始／停止／リセット。工程に
  `timer_sec` があれば既定値として出す。複数同時。終了は音と板の点滅
- **動画の板（P4）に着手**: 主人の `Assets/TLab/Youtube/Prefab/YoutubePlayer.prefab`（`TLabWebView`）を包む


### 2026-09-13 主人の実機確認（v1.0.6）

主人の言葉（UI の4件）と、それに対して決めたこと。

**大元の原因（4件に共通）**: Unity 既定の runtime テーマ（`UnityDefaultRuntimeTheme`）は `Label` に
**padding 上下 4 px・margin 上 4 px 下 2 px**（計 14 px ≒ 2.8 cm）、`Button` にも padding を入れる。
1 UI px = 1 画面 px の普通の UI 向けの値だが、**1 UI px ≒ 2 mm** のこの板では**文字より余白のほうが
大きい**——本文 6 px（字の高さ ≒ 8.7 px）の Label が 23 px を占め、4 cm のつもりのボタンが 5.8 cm に
なっていた。「行間が空いている」「1画面に収まらない」「進捗が上を結構多く占めている」はいずれも
これが元。`theme.uss` の `.heading` / `.body-text` / `.caption` / `.kitchen-button` で余白を落とし、
間隔は各板の USS で**要る場所にだけ**明示する方針にした。

1. **工程の板を1画面に**（主人「スクロールは極力なくしたい／進捗の縦幅をもう少し狭く／画像が 1x1 なので
   左右に分けたほうが」）。頭は**工程の点列（4 px）と進捗%だけ**にして 8 px まで薄くし、「一覧へ」「配置」は
   下のボタンの行の左へ移した（`[一覧へ][配置] …余白… [戻る][次へ]`。2度押しはそのまま）。
   本文は ScrollView をやめ**左右2列**——左に工程画像（**正方形**。契約の工程画像は 1:1 なので、
   縦に積むと幅いっぱいまで伸びて説明を押し出す。一辺は本文の高さ＝ 131 px ≒ 26 cm）、右に見出し・説明・
   材料の札・「次: …」。材料の札は画像に重ねず説明の下へ。板は **260×190 のまま**（左右2列にしたら収まった。
   幅を 280 にすると初期配置でタイマーの板との重なりが 2 cm → 6 cm に増えるので広げていない）。
2. **材料の板を一目で全部**（主人「縦スクロールしないと全部見れないのは UX 的に…／行間が空いている／
   もうちょいパネルは大きくても」）。行は `min-height` 12 px → **8 px**・行間 0（実測の送りは 11 px ≒ 2.2 cm）、
   板は 150×190 → **170×240**（34 cm×48 cm）。見本の炒飯（13 点）はもちろん **16 点でもスクロール無しで
   全部見える**（PlayMode で検算）。設計 §7 の「最小4 cm角」は押す釦の話で材料の行には当てない——
   押し損ねても調理は進まない見た目であり、一目で全部見えることのほうが効く。
3. **レシピ一覧を写真付きグリッドに**（主人「Web のレシピサイトと同じようにグリッドで写真も」）。
   行 → **3列のカード**（上に写真・下に題名 2 行と「25分 ・ 中華 ・ 620kcal」）。押す的はカード全体。
   写真（hero）は工程の画像と同じ経路——`RecipeStore` 越しに**ローカルから**読み、無ければその場で取りに行き、
   取れなければ「写真なし」の下地のまま（オフライン前提。取得は UniTask で、並べ直し・板の破棄では
   CancellationToken で断って Texture2D を捨てる）。写真の高さは **34 px**（約 2:1）——16:10 にすると
   2 段目（4〜6 件目）が 7 px はみ出すので、「**6 件までは縦スクロール無し**」を優先した。頭に「設定」を足した。
4. **表示の設定**（主人「パネルサイズと文字サイズですが、設定とかで変更できたらもっといい」）。
   一覧の板の「設定」で、一覧と**入れ替わりに**設定の区画が出る（板は増やさない。増やすと置き場所を
   覚える対象も増える）。「文字の大きさ [小][中][大]」「板の大きさ [小][中][大]」「閉じる」の3行だけで、
   押した瞬間に反映して保存（「決定」は置かない。設計 §7）。
   - **文字**は板の `.root-panel` に `font-scale--small` / `font-scale--large` を付け外しし、
     `theme.uss` がその配下で `--font-size-*` を上書きする（小 ×0.85・大 ×1.25。中はクラス無し）。
     カスタムプロパティは継承するので、板の中のボタンの文字も材料の行も一緒に動く
   - **板**は `transform.localScale` を `WorldSpacePanelFactory.PanelLocalScale` × 係数（小 0.85／中 1.0／
     大 1.2）にする。コライダーはローカル単位なので一緒に拡縮され、押せる場所もずれない。原点は左上なので
     右下へ伸びる
   - 置き場は `Application.persistentDataPath/settings.json`（`{"font_scale":…, "panel_scale":…}`）。
     **壊れていれば黙って既定（中・中）に戻す**——設定が読めないことで起動を止めない。
     アンカーも控え（`panels.json`）も位置と向きしか持たないので、**縮尺は起動のたびに設定から当て直す**
   - 当てる先は調理と一覧の4枚（一覧・レシピ・材料・タイマー）。動画の板は自分で寸法を決める（16:9 ⇄ 9:16）
     ので混ぜない。配置の操作板と手のひらメニューも、出ている間だけの板なので対象外

#### 動画の板と効果音（同日・執事）

- **効果音**: ボタン（行）が発火した瞬間に短い音を1つ鳴らす（`PressSound`。`PokePress.Pressed` を聞く。`Resources/Audio/press.wav`、70ms、2D）。板ごとに置かず起動時に自分で1つ立つ——シーンの組み立てに手を入れないため。ホログラムには手応えが無く、振動は hover で既に出ているので、「発火した」の返事は音が担う。
- **動画の板の札（3行）**: 主人「原因が分からないのでエラーや失敗時にどこかに表示してほしい」。1 行目は WebView／HTML／YouTube の状態と絵の実寸（`窓 …u → 絵 …mm`）、2 行目は板が最後にしたことと主人の `YoutubePlayer` が出した最後のログ、3 行目（赤）は最後の失敗。`IVideoPlayer` に `StatusLine` / `LastError` / `IsReportedPlaying` / `TapCenter` を足した。
- **「再生」は選ぶ前でも押せる**。v1.0.4 は選ぶまで無効にしていたが、無効の Button はポークも受けず「押しても反応がない」に見えた。`youtube.html` は既定の動画を cue して立つので、押せばそれが始まる。
- **人の操作なしの再生への対策**: Android の WebView は既定で `mediaPlaybackRequiresUserGesture` のため、JS の `play()` だけでは拒まれ得る（主人の SDK は WebView そのものを触る前提なので困らないが、この板は触らせない）。再生を頼んで 0.9 秒しても YouTube が PLAYING/BUFFERING を返さなければ、`TLabWebView.TouchEvent` でページの中央を一度タップする（cue 中の中央は YouTube の大きな再生ボタン）。一度でも操作が入れば以後の JS は通る。
- **絵の寸法の直し**: 窓の実測（`worldBound`）は板のローカル単位（UI px ÷ 100）で返る。v1.0.4 はここに板の縮尺 0.2 を余計に掛けていて、絵が本来の 1/5（幅 10cm）だった——主人の「サムネイルが小さく表示されていた」はこれ。
- **一覧の行**を 1.6cm → 2.8cm（指先の当たりに対して薄すぎた）。押し込みの見た目も付けて「押せたか」が分かるように。

### 2026-09-13 主人の実機確認（v1.0.7）— 配置とレイ

主人の言葉（配置モードとレイ操作の4件）と、それに対して決めたこと。

1. **配置の操作を全部「手のひらメニュー」へ**（主人「配置の起動は手元でできるが、確定や終了は手元じゃなく
   3D 空間に配置されていて、ほかのパネルと重なるとレイでボタンが押せなくなる」）。
   頭の前に出していた操作板（200×90）は**廃止**し、手のひらメニューを 70×36 → **130×92**（26cm×18.4cm）に
   広げて、調理中は「配置」1つ、配置モード中は「保存」「元に戻す」「板を手元に」「やめる」＋ヒント1行を出す。
   `PlacementPanel.cs` / `.uxml` は削除、`PlacementPanel.uss` は `PlacementMenu.uss` に改名。
   手のひらメニューは**重なる相手が居ない**ので、板がどこにあっても操作が届く。
   出口がこの板にしか無くなったので、**この板が挿さっていなければ配置モードへ入らない**
   （行き止まりを作らない。EditMode 試験で在ることを必須にした）。
2. **メインパネルが掴めなかった本当の原因**は2つ重なっていた。
   (a) 上の空間の操作板がレシピ／一覧の板と重なって当たり判定を奪っていた（1 で消えた）。
   (b) 同じ鍵に重ねた2枚（レシピと一覧）のうち、**掴む仕掛けと黄色い枠を leader（レシピの板）に
   付けていた**。起動直後に見えているのは一覧の板なので、主人が掴もうとした板は掴めず、
   しかも2枚のコライダーが完全に重なるのでどちらが引き当てられるか運任せだった。
   → 配置モードに入るとき **見えている板を取っ手役に選び、同じ鍵の残りは引っ込める**
   （`PanelPlacement.Entry.Handle`）。保存で覚えるのも取っ手役の姿。PlayMode で検算。
3. **レイの切り替えを「on/off」から「層」へ**（主人「配置のときはレイ操作を有効にしてほしい」
   「Youtube プレイヤーだけレイ操作を有効化してほしい」）。Ray（Near-Far Interactor）は
   **どちらのモードでも生かしたまま**にして、届く先を2つの層で絞る:
   - **Interaction Layer**（XRI。`InteractionLayerSettings` の 1 番に `Video` を足した）——
     動画の板の `XRSimpleInteractable` だけが `Default | Video` を名乗る。
     Ray の `interactionLayers` は 調理モード＝`Video` だけ／配置モード＝全部。
   - **物理層**（`TagManager` の 8 番に `Kitchen Panel Off Ray` を足した）——
     Interaction Layer は **UI Toolkit の当たりには効かない**（XRI のワールド空間 UI は
     「レイが当たったコライダーに UIDocument が付いているか」しか見ない）ので、それだけだと
     調理中にレイを向けて摘まむだけでレシピの「次へ」が押せてしまう。そこで調理モードの間、
     **動画以外の板を 8 番へ移す**。Ray の `raycastMask` は Default(0)/UI(5)/XR Simulation(31) なので
     8 番には届かず、`Physics.DefaultRaycastLayers` には入ったままなので**指では押せる**。
     （2 番 "Ignore Raycast" ではだめ。そこは DefaultRaycastLayers から外れるので
     ポークの UI まで死ぬ——PlayMode 試験で実際に踏んだ）。
   Poke・Gaze・Teleport の Interactor は**触らない**（v1.0.7 は Gaze/Teleport も一緒に止めていた）。
   Gaze は `allowGazeInteraction`（既定 false）を立てた板にしか効かず、Teleport の層は 31 番だけなので、
   生きていても台所の板には何もしない。
4. **「板を手元に」**（主人「壁の奥に行ってしまったらつかめないので何とかしたい」）。
   配置モード中の手のひらメニューの釦。押すと**全部の板**を、今の頭の向きから決めた初期配置
   （起動時と同じ並び）へ並べ直す。配置モードは続いたままなので、そのまま掴み直して「保存」できる。
   設計 §7 の「取り消せない操作は2度押し」には当たらない——「元に戻す」で入る前の位置へ戻せるので**1度押し**。

**壁のコライダーについて（調べた結果）**: 台所の板が壁に遮られていたのでは**なかった**。
MR テンプレートの `ARPlaneManager` と `ARBoundingBoxManager` はプレハブで無効（有効にしていたのは
消したチュートリアル UI のトグル）で、そもそも AR のコライダーが立っていない。仮に立っても
`ARPlane` のメッシュコライダーは 7 番 "Placeable Surface" で、Ray の `raycastMask`（0/5/31）に
入っていないので遮らない。`ARBoundingBox` は 0 番なので遮るが、こちらも無効のまま。
掴めなかったのは上の 1・2 が原因と見ている。

**rig の Interactor（2026-09-13 に数えた）**: `MR Interaction Setup/XR Origin (XR Rig)/Camera Offset/` の下に
`Left Hand`・`Right Hand`・`Left Controller`・`Right Controller` が居て、それぞれに `Near-Far Interactor` が1つ
（計4つ。**手でもコントローラでも効く**）。ほかに `Gaze Interactor` 1つ・`Teleport Interactor` 2つ、
左右の手に `Poke Interactor` 1つずつ。Near-Far の far 側は `farAttachMode = Far`（＝遠くで掴んでも板は
その場に留まり、手の前後の動きで引き寄せる）で、`castDistance` は 10m。

#### 動画の板・一覧・工程画像（2026-09-13 主人の実機確認 v1.0.7・執事）

- **薄白いカバー**: 板（UI Toolkit）と絵（uGUI の RawImage）はどちらも深度を書かない半透明で、描く順はカメラからの距離で決まる。0.6mm では板が後に描かれ、白 85% の地が絵に乗った。直し2つ——絵を **1cm** 手前へ（当たり判定は板の面のまま）、板の地を透明にして白い地は上下の塊（一覧・操作部）にだけ塗る。窓の部分には塗るものが無いので、順が入れ替わっても覆われない。
- **向きの切替で板が動く**: 覚えていた下辺の中央（起動時の点）は、配置モードやアンカーの復元で板が動いたあとは古かった。寸法を変える直前に今の Transform から取り直す。
- **同梱 `media.json` の更新**: v1.0.7 までは初回だけ写して以後は手元しか見なかったため、主人が同梱を直して APK を入れ替えても一覧が変わらなかった。毎回同梱も読み、前回写した控え（`media.bundled.json`）と違えば「APK 側が変わった」と見なす。手元が控えと同じなら置き換え、違えば（主人が端末側を直した）手元を守る。
- **工程画像**の四方に 1cm の余白（主人「少し大きすぎ」）。一辺から余白ぶんを引くので列幅は変わらない。
