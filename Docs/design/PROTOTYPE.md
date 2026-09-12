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
| 入り方 | 手元のメニューの「配置」、またはレシピ／一覧の板の「配置」（2度押し） | 起動時・配置を「保存」した後 |
| 手の入力 | Ray ＋ Grab（掴んで動かす・遠くから引き寄せる） | Poke（触る）＋ **動画の板にだけ Ray** |
| 画面 | **見えている板**に枠と取っ手が出る（重なった裏の板は引っ込める） | 枠なし |
| 操作の置き場 | **手元のメニュー**（保存・元に戻す・板を手元に・やめる） | メニューは「配置」1つ |
| 出方 | 「保存」→ アンカーと控えへ／「やめる」→ 元に戻す | — |

手元のメニューは**手首の釦**で出し入れする（左右の手首の手のひら側に 4.4cm 角の板が付けっぱなし。
押すとトグル。閉じているのが既定）。v1.0.8 までは XRI の `HandMenu` が手のひらの向きで出していたが、
手を洗っている最中にも出たのでやめた。

（2026-09-13 v1.0.7 の実機確認で作り直し、v1.0.8 で手首の釦にした。
理由と仕組みは §11 の「配置とレイ」と「手首・レイ・動画の窓」を見よ。）

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

- v0: 動画パネル＝WebView 1枚。操作は「URL を開く／再生・停止／音量／戻る」だけ（主人が「キーボード
  入力周りは若干使いにくかった」と言っているので、**文字入力はパネルに置かない**。開く URL は
  サーバの `GET /media` の一覧から選ぶ。一覧は PC 側で書く）
- **窓（WebView）そのものを触れる**（2026-09-13 の方針3。§11 の v1.0.8 の追補）。触った場所を
  そのまま WebView へ渡すので、`youtube.html` が一時停止・終了時に出す**関連動画**をレイでもポークでも
  選べる。次の動画を板の一覧に持たせない代わりの道で、文字入力を置かない方針とも噛み合う
  - 触りは**タップとドラッグを板の側で見分ける**（v1.0.9 の直し。§11）。揺れをそのまま DRAG で
    流すと WebView がスクロールと解釈してクリックを出さないので、閾値を越えるまで DOWN を送らない
  - 関連動画が `youtube.com/watch` へ出てしまったら、URL から **video_id を抜いて**
    埋め込みプレイヤーへ連れ戻す（主人の提案。§11 の v1.0.9）
- 一覧（再生リスト）は**窓の右横に縦並び**（サムネ＋題名。v1.0.9。主人「YouTube を Web で見るときの
  画面みたいに」）。サムネイルの URL は manor の一覧（ADR-016 の `thumbnail_url`）から、
  無ければ動画 id から組み立てる
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
- **指先の光る点**（v1.0.9。主人「指が UI に近づいたときだけ…これがあるだけで、奥行きの距離感が
  一気につかみやすくなります」）。板の 5cm 以内に入ったときだけ人差し指の先に 8mm の琥珀の点を出し、
  面に近いほど大きくする（＝距離計）。押した瞬間だけ一瞬強く光る。
  **近づいていないときは出さない**のが原則——常に出せば料理中の手にいつも付いて回る。仕組みは §11
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

### 追補（v1.0.10・2026-09-13）— 繋ぎ方は端末のペアリングへ

上の「繋ぎ先と合言葉はファイルで渡す」は**合言葉の部分だけ廃止**した（正は manor の
ADR-017。主人「合言葉を平文でファイルに置きたくない」）。今の繋ぎ方はこう:

| 口 | 何 |
|---|---|
| `POST /api/v1/devices/pair/start {name, kind}` | 認証なし。返り `{pair_id, code(6桁), expires_in, poll_after}` |
| `POST /api/v1/devices/pair/poll {pair_id}` | 認証なし。`{status: pending\|approved\|expired, token, device_id, user_id}` |
| `Authorization: Bearer <鍵>` | 以後の `/api/v1/kitchen/*` はこれで叩く（cookie のログインはしない） |
| UDP 8791 に `manor-discover v1` | 探索。返り `{kind:"manor", name, base_url, version}` |

- **番号は端末に出し、主人が Web（設定 → 端末）に入れる**——板に文字入力を置かない（§6）以上、
  向きはこちらしかない。番号は一覧の板の覆い（`RecipeListPanel.ShowPairing`）に 20px（実寸 4cm）で出す。
- 鍵は `persistentDataPath/manor-device.json`（`base_url`・`token`・`device_id`・`user_id`・`paired_at`）に控える。
  **401 が返ったら鍵を捨てて番号を出し直す**（入り直しはできない。やり直しとは主人に許可を貰うこと）。
- 繋ぎ先の順は `manor.json` の `base_url`（明示の上書き。tailnet 越しなど）→ 控え → 探索。
  `manor.json` の `passcode` は**読まない**（見つけたら警告を1行）。
- cookie の経路（`/auth/login`）は口としては残してあるが、`manor.json` からは通らない
  ——試験と、将来 tailnet 越しに cookie を使う場合のため。
- 手順と札の読み方は [`Docs/manor-connection.md`](../manor-connection.md)。

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

### 2026-09-13 主人の実機確認（v1.0.8）— 手首・レイ・動画の窓

主人の言葉（4件）と、それに対して決めたこと。

1. **手のひらメニューを「手首の釦」へ**（主人「手のひらメニューはいい感じですが、手を横に向けている
   ときにも表示されています。手を洗ってるときなどに出ると邪魔なので、要改善。手首の手のひら側に
   ボタンを作っておいて、それを押すとパネルがトグルでも全然いいと思います」）。
   XRI の `HandMenu` をやめた。あれは**手のひらの向き**を合図に出し入れするが、サンプルの設定
   （`Menu Hands Follow Preset`）の閾値は「手のひらが自分を向いている 75.7°」かつ
   「手のひらが上を向いている **95.7°**」——後者は水平より広いので、手を横に向けても成立する。
   閾値を詰めても解決しない。**手の姿勢を合図にしている限り、料理中の手は必ずどこかで合図を出す**
   （§7 の原則「料理中の手は UI 操作の意図ではない」）。
   - 代わりに `WristMenu`＋`WristMenuButton`。左右の手首の手のひら側に **4.4cm 角の板**（`WristToggle`）を
     1枚ずつ付けっぱなしにし、反対の手で押したときだけ 130×92 のメニューが**トグルで**出る。
     左右どちらでも押せるのは `HandMenu` の既定（`MenuHandedness.Either`）と同じで、メニューは
     **最後に押した側の手**に付いて出る。
   - 手のひらの Transform の取り方は `HandMenu` と同じ（`Left Hand`／`Right Hand` の下の `Palm`）。
     手のひらの軸は同じ preset から読める——`palmReferenceAxis: 4`（Down）なので
     **手のひらの面が向く先 ＝ `-palm.up`**、**指先 ＝ `+palm.forward`**、**手首 ＝ `-palm.forward`**。
     釦は手首側へ 7cm・面から 2cm、メニューは面から 14cm・指先側へ 10cm（掌の上の空間）。
     **釦とメニューを 17cm 離す**のが肝心——近いと、釦を押しに来た指がメニューの当たり判定
     （板の裏へ 24cm の箱）に先に触れて釦が押せない（PlayMode 試験で踏んだ）。
   - メニューは**視線の側を向けて**出す（手首をひねった角度がそのまま板の角度になると読めない）。
   - **釦はレイの相手にしない**——物理層 8 番 `Kitchen Panel Off Ray` に置く。
     Ray の `raycastMask`（0/5/31）から外れ、`Physics.DefaultRaycastLayers` には入ったままなので
     「指では押せるが遠くからは押せない」。`CookingModeInputGate` には登録しない（層を戻されては困る）。
   - 手を見失えば釦もメニューも引っ込む。配置を「保存」「やめる」で終えると**自動で閉じる**。
     逆に、レシピ／一覧の板の「配置」から入るときは `Bootstrap` が**必ず開ける**——
     閉じたままだと「保存」に手が届かない。**手が1つも追えていなければ配置モードへ入らない**
     （コントローラだけのときがこれ。§7「行き止まりを作らない」）。
2. **レイの線は相手にホバーしている間だけ**（主人「レイが操作できない場合は表示を消してほしいですね。
   Youtube パネルにレイが当たっているときだけ表示はできますか？」）。
   調理中、手から 25cm の線（`CurveVisualController.restingVisualLineLength`）が常に出ていた。
   - **XRI 3.5 に「無効なときは隠す」の設定は無い。** `CurveVisualController` は
     `LineDynamicsMode.RetractOnHitLoss` で短くできるだけで消せず、しかも component を無効にしても
     `OnDisable` が `LineRenderer` に触らないので**最後の線が固まって残る**。
     そこで `RayLineVisibility`（小さな MonoBehaviour）が `hasHover`／`hasSelection` を見て、
     描き手と `LineRenderer` の**両方**を on/off する。
   - 見るのは XRI の層（`interactionLayers`）を通った後の状態なので、モードの分岐は要らない——
     調理モードでは動画の板だけ、配置モードでは全部の板が自然に「相手」になる。
   - 手とコントローラの **Near-Far 4つ全部**に付ける（`KitchenSceneBuilder` が配線し、EditMode 試験が数える）。
3. **動画の窓（WebView）への触りを通す**（相談で決めた方針3「次の動画は埋め込みプレイヤー自身の
   関連動画で選ぶ。動画の窓への触りをレイ（とポーク）で通す」）。
   `youtube.html` は一時停止・終了時に自分で関連動画を出すので、そこを触れるようにすれば
   板に一覧を持たなくても次が選べる（**文字入力は置かない**。§6 のまま）。
   - `VideoPanel` が `videoArea` の `PointerDown`／`PointerMove`／`PointerUp` を拾い、
     **絵の中の比**（u は左→右、v は上→下の 0〜1）に写して `IVideoPlayer.Touch(phase, u, v)` へ流す。
     `YoutubePlayerBridge` がそこに `webWidth`／`webHeight` を掛けて `TLabWebView.TouchEvent` を送る
     （主人の `WebViewInputListener` と同じ経路。段の番号 DOWN=0・UP=1・DRAG=2 もそのまま）。
     解像度は向きで変わるのでそのつど読み直す。
   - **窓は釦ではない**ので `PokePress`（押し下げで発火・600ms 間引き・離れるまで次を受けない）は
     通さない——WebView にクリックと見なしてもらうには Down →（Drag）→ Up を素直に流す必要がある。
     板のボタン（再生・音量・向き）はこれまでどおり `PokePress`。
   - 窓と絵は同じ大きさではない（絵は目当ての比で窓に収まるので余白が出る）。**絵の外は送らない**。
     離脱（`PointerLeave`／`PointerOut`／捕捉の解除）でも必ず Up を送る——
     送り損ねると WebView の中で指が押されたままになる。
   - **「戻る」**（`TLabWebView.GoBack`）を操作部に足した。関連動画を触った先で youtube.com 本体へ
     遷移すると埋め込みプレイヤーの JS が効かなくなるため、帰り道を1つ置く。
4. **横に置いた板へレイが向かない**（主人「Youtube のパネルはレイ操作反応しましたが、横に
   （正面と垂直になるように）配置した時に、レイが正面に吸われてしまって、Youtube パネルに
   近づいてもレイはそっちに向きませんでした」）。**調べた結果は次のとおり。**

   **主因（確度 高）: ハンドトラッキングの aim（照準）姿勢が体に紐づいている。**
   手のレイの向きは手の向きではなく、`Camera Offset/<手>/Aim Pose` の `TrackedPoseDriver` が読む
   `Aim Position`／`Aim Rotation`——結び先は
   `<MetaAimHand>/devicePosition`・`<HandInteraction>/pointer/position`・`<XRHandDevice>/aimPosition` で、
   いずれも**システムが作る照準（肩あたりから手を通る向き）**。手首をひねっても向きは変わらず、
   真横を指すには手ごと体の横へ出すか、体を向ける必要がある。さらに Quest のカメラの視界から
   手が外れると `TrackedPoseDriver` は**最後の姿勢を保つ**ので、レイは正面を指したまま固まる
   ——主人の「正面に吸われる」はこれと見ている。
   **これは設定では直せない**（直すなら照準の出どころを手のひら／人差し指の向きに差し替えることになり、
   レイの手触りが全部変わる。今回は入れない）。

   **副因（確度 中・直した）: far 側の caster の姿勢の安定化。**
   `CurveInteractionCaster` は `m_EnableStabilization = 1`・`m_AimTargetObject` に**自分自身**が
   挿さっていて、`XRTransformStabilizer` は「**前のフレームの着地点を保つ回転**」へ寄せるほうが
   安ければそちらを選ぶ。効き幅は `angleStabilization × clamp(1 + ln(rayLength), 1, 3)` で、
   **何にも当たっていないとき**の着地点は 10m 先の空なので係数は上限の 3 ——
   20° の設定が実効 **60°** まで広がり、空を薙いでいる間ほどレイが渋くなる。
   `KitchenSceneBuilder` がシーンを組むときに **20 → 8** へ緩めた（手の震え 1〜3° は今でも吸う）。
   効かなければ次の手は、同じ caster の `Aim Target Object` を空にすること。

   **無関係と分かったもの**:
   - **Gaze の aim assist**: `Gaze Interactor` は `m_IsActive: 0`（無効）で、`GazeAssistanceSnapVolume` も未設定。
   - **Teleport**: `interactionLayers` は 31 番だけ、GameObject も `Locomotion` ごと無効。
   - **`snapEndpointIfAvailable`**: これは `XRInteractorLineVisual` の設定で、
     rig でそれを使っているのは Teleport の2本だけ。Near-Far は `CurveVisualController` を使う。
   - **正面の板が相手を奪っている**のではない。調理モードでは動画以外の板は物理層 8 番へ移り、
     Ray の `interactionLayers` も `Video` だけになる（PlayMode 試験
     `調理モードではRayが動画の板だけに届きPokeが効く` が毎回確かめている）。
   - **cone cast**（`m_HitDetectionType: 2`・`m_ConeCastAngle: 6`・10m）は正面寄りに当たりを広げるが、
     真横（90°）の板を拾えない理由にはならない。

   **実機での確かめ方**（主人へ）:
   - 手のひらを上に向けたまま**手首だけ**を左右に振る → レイがほとんど動かなければ主因の裏付け。
     次に**手ごと体の横へ出す** → レイが付いてくれば「体に紐づいた照準」で確定。
   - 動画の板を**真横ではなく 30〜45° 内向き**に置くと、少し体を向けるだけで指せるはず。
   - 手をカメラの視界の端（真横・体の陰）へ持って行くとレイが固まるか（＝トラッキングが切れて
     最後の姿勢を保つか）。固まるならカメラの視界の問題。

#### 動画の一覧は manor が正（2026-09-13 主人の裁定・執事）

- 主人「manor 側で一覧を持って、API で取得」。manor 側は ADR-016（`chef_media`・`GET /api/v1/kitchen/media`・料理長の Web の別ページ「動画リスト」）。XR は起動時に一覧を取って `persistentDataPath/media.json` へ写し、圏外ならその写しを読む（レシピ帳の index.json と同じ流儀）。編集は Web だけ——板に文字入力を置かない設計（§6）はそのまま。
- 同梱の `media.json` は manor 未設定のときの見本に格下げ。v1.0.8 で主人が「同梱を直したのに一覧が変わらない」を踏んだのは、控えが無いときに手元を守っていたため。控えが無ければ同梱を正とするに改めた。
- 「次の動画」は埋め込みプレイヤー自身の関連動画で選ぶ（方針3。窓への触りをレイとポークで通す）。YouTube Data API は Web 側の取り込み・検索に使う（将来の ADR）。関連動画 API は廃止済みなので XR で再現しない。

### 2026-09-13 主人の実機確認（v1.0.9）— 動画の板と指先

主人の言葉（4件）と、指先カーソルの求め。**動画の板と指先だけ**の回で、他の板は触っていない。

1. **関連動画のタップが効かない**（主人「画面の直接タッチや、関連動画を開いて横にスクロールは
   できるようになった。ただ関連動画をタップしても反応しない（遷移はしない）。リンクへの遷移は
   WebView の仕組みだとそのまま動作しないかな？？ もしそうなら関連動画の video_id だけ抜いて、
   ロードできるようにとかできないか」）。

   **原因は「タップがドラッグになっていた」（確度 高。主人の症状の形と一致する）。**
   v1.0.8 で入れた窓への触りは `PointerDown` → DOWN、`PointerMove` → DRAG、`PointerUp` → UP を
   そのまま流していた。レイの着地点は手の震え（1〜3°）が 1m 先で 2〜5cm に開き、ポークの指先も
   同じだけ揺れる。だから押し下げと押し上げの間に**必ず** DRAG が挟まり、Android の WebView は
   これを「スクロール」と解釈してクリックを出さない。
   「横スクロールはできるがタップは効かない」という主人の観察は、**まさにこの形**だけを説明する
   ——DOWN も DRAG も UP も届いているのに、クリックだけが出ていない。

   直し（`VideoPanel.BindVideoAreaTouch`）:
   - **押し下げでは何も送らない。** 位置と時刻だけを覚える。
   - 動きが **絵の幅の 3%**（`TapMoveThreshold`。50cm の絵で 1.5cm）を越えたら、そこで初めて
     押し下げの座標で DOWN を送り、以後 DRAG を流す（＝スクロール。主人の「横スクロールはできる」を守る）。
     縦の揺れは絵の縦横比を掛けて同じ物差しで測る（9:16 では縦 1% が横 1.8% ぶん）。
   - 越えないまま **1.2 秒**（`TapMaxSeconds`）以内に離れたら**タップ**。
     `IVideoPlayer.Tap` が **DOWN → 80ms → UP を同じ座標で**送る（`TapCenter` と同じ作法）。
     主人との取り決めは 400ms だったが 1.2 秒に広げた——ホログラムを指で突くと指は面を通り抜けてから
     戻るので PointerDown〜PointerUp に数十フレーム掛かり、400ms では実機の普通の突きが「長押し」に
     落ちる。広げても WebView 側は**常に 80ms のクリック**しか見ないので（押し下げと押し上げを
     こちらが作る）Android の長押し＝文脈メニューにはならず、害が無い。
   - 越えないまま長く留まって離れたら**何も送らない**（板に手を置いただけ）。
2. **遷移そのものへの備え**（主人の提案どおり **video_id を抜く**）。埋め込みプレイヤーの関連動画は
   2種類ある——同じ iframe の中で再生されるもの（何もしなくてよい）と、top frame を
   `youtube.com/watch` へ運ぶもの。後者は `youtube.html` の JS（`loadVideo`・`play`）が丸ごと消えるので、
   板からは以後何も操作できない（v1.0.8 の「戻る」はそのための逃げ道だった）。
   - `VideoPanel` が **0.5 秒ごとに `TLabWebView.GetUrl()`** を読む（`IVideoPlayer.CurrentUrl`）。
     `http://localhost`（主人の html の baseUrl）の外で `watch?v=`・`youtu.be/`・`/shorts/` を
     含んでいたら、`MediaJson.NormalizeVideoId` で id を抜き、
     **html を読み直して**（`YoutubePlayer.LoadHtml(Html.text)` を反射で。効かなければ `GoBack`）
     1.8 秒待ってから `Load(id)`。札に「関連動画 &lt;id&gt; を開きました／読み込みました」と出す。
   - html を読み直すときは主人の `videoId` も反射で空にする——`LoadVideo` は「同じ id なら
     何もしない」で帰るので、忘れさせないと読み直した直後に同じ動画を頼めない。
   - `GetUrl()` は `m_state` を見ないので、**初期化を確かめてから**呼ぶ（でないとネイティブの口が無くて例外）。
3. **16:9 で縦にスクロールできてしまう**（主人の言葉のまま）。主人の `youtube.html` は
   `body { height:100%; overflow:hidden }` だが、Android の WebView は端でオーバースクロールし、
   DRAG を流すとページごと動く。**html は書き換えない**ので、読み込み完了後と向きを変えるたびに
   JS を送る（`IVideoPlayer.SuppressPageScroll`）——html と body の `overflow:hidden`・
   `overscroll-behavior:none`・`touch-action:none` に、`touchmove` を **body と html の上でだけ**
   `preventDefault` する聞き手を1つ。**iframe の中は素通し**にするのが肝心で、
   でないと主人の「関連動画を開いて横にスクロールはできるようになった」を殺してしまう。
   1 の「閾値までは DRAG を送らない」もここに効く（揺れだけではページが動かない）。
4. **一覧を右横に縦並び**（主人「再生リストは、今はメイン画面の上にあるけど、レイアウト的に
   右横に置いて縦スクロールできた方がいいかも（YouTube を Web で見るときの画面みたいにサムネ＋タイトル）」）。
   板は「**左に窓・右に一覧・下に操作部**」になった。
   - 寸法（`VideoPanel` の定数と `VideoPanel.uss`、EditMode 試験が数字を突き合わせる）:
     16:9 は **354×224 px ≒ 70.8×44.8cm**（窓 250px＝**50cm** ＋ 間 4px ＋ 一覧 100px＝20cm）、
     9:16 は **206×264 px ≒ 41.2×52.8cm**（窓 102px、絵の高さ 181px＝36cm）。
     窓の高さに 20px の取り置きがあるのは、札3行がそれぞれ2行に折り返したときのぶん
     ——削ると実機で札が伸びた瞬間に絵が 50cm を割る（PlayMode 試験が上下両側から縛っている）。
   - 一覧の幅は**向きが変わっても変えない**（並んでいるものの位置が動くと探し直すことになる）。
   - 行は「左にサムネイル（44×25px ≒ 8.8×5cm。16:9）＋ 右に題名（2行で打ち切り）」。
     行の高さが 2.8cm → 5cm になったので、v1.0.6 の「一覧の行が押せない」からさらに遠ざかった。
   - **サムネイルの URL** は manor の一覧（ADR-016 の `thumbnail_url`）から。無ければ
     `https://i.ytimg.com/vi/<video_id>/hqdefault.jpg` を `MediaJson` が組み立てるので、
     **板は「絵が無い行」を作らずに済む**。取得と保存は `Presentation/Video/MediaThumbnailCache`
     （`persistentDataPath/media-thumbs/<video_id>.jpg`・取得口は `Net/IRecipeImageDownloader` を借りる・
     **失敗は黙る**・`Texture2D` は板が消えるときと並べ直すときに捨てる）。
     絵は id ごとに決まっていて変わらないので、一度取れば以後は通信しない。
5. **指先カーソル**（主人「指が UI に近づいたときだけ、人差し指の先端に小さな『光るドット
   （カーソル）』や UI 側のハイライトを表示させてください。これがあるだけで、奥行きの距離感が
   一気につかみやすくなります」）。`Presentation/FingertipCursor`。
   - 直径 **8mm** の球、色は theme の琥珀（`#F59E0B`）、材質は Unlit（＝光に当たらず、そのまま光って見える）。
     球と材質は**実行時に作る**のでプレハブも資産も増えない。作った球の**コライダーは必ず消す**
     ——残すと下の距離計が自分の点を拾い、ポークの当たり判定にも混ざる。
   - 出す条件は「板に **5cm** 以内へ近づいたとき」だけ。常に出すと料理中の手にいつも点が付いて回る
     （§7「料理中の手は UI 操作の意図ではない」）。
   - **`XRPokeInteractor.hasHover` をそのまま使わない。** あれは `pokeHoverRadius`（既定 1.5cm）の
     球の重なりで決まるので、「もう触れる」ところでしか立たず主人の言う「近づいたとき」に間に合わない。
     `pokeHoverRadius` を広げる手も採らない——XRI のホバー（色・振動）まで 5cm 手前で始まり、
     触っていない板が反応してしまう。だからこの部品が自分で 5cm の球を撫でて、
     **`XRBaseInteractable` を持つコライダー**（＝板）までの距離を測る。壁や鍋では光らない。
   - 大きさが距離計: 5cm で 0.4 倍（≒3mm）、板の面で 1.0 倍（8mm）。
     押した瞬間（`hasSelection` が立った瞬間）だけ 1.6 倍を 0.12 秒
     ——ホログラムには手応えが無いので、「今 押した」の返事が効果音とこれしかない。
   - 左右の手とコントローラの **Poke Interactor 4つ全部**に付ける（`KitchenSceneBuilder` が配線し、
     EditMode 試験が数え、PlayMode 試験が「近づいたときだけ・近いほど大きい」を検算する）。
   - **板の側のハイライトは触らない**（主人の指示どおり、XRI の既存のホバー色で足りている）。
6. **札**（`actionLine`）に 1 と 2 の判定結果が出る:
   「窓: タップ（u, v）」／「窓: ドラッグ開始（u, v）」／「窓: ドラッグ終了」／
   「窓: 長押し n.n秒（何も送りません）」／「関連動画 &lt;id&gt; を開きました／読み込みました」／
   「窓の縦スクロールを止めました（html 読込後）」。
   実機で 1 の直しが効いたのか、それとも 2 の遷移が起きているのかを主人が見分けられるようにした。
