# manor との連携

KitchenXR は AI Manor（manor）の**レシピ帳**と**動画リスト**を読む側です。契約の正は manor の
`docs/design/ADR-015_recipe_book.md`（レシピ帳）・`ADR-016_media_list.md`（動画リスト）・
`ADR-017_device_pairing.md`（ペアリング・探索・LAN）で、ここには **XR が使う口だけ**を写します。
登録・取り込み・編集は manor の Web の仕事で、XR からは行いません。

## 1. 繋ぎ方（ペアリング）

板に文字入力を置かない設計なので、**番号は端末に出し、Web で許可します**。

1. Quest で KitchenXR を起動すると、一覧の板の覆いに **6 桁の番号**が実寸 4cm で出ます
   （`RecipeListPanel.ShowPairing`）。
2. manor の Web を開き、**設定 → 端末**でその番号を入れ、利用者を選んで「許可」。
3. 板の番号が消えて、レシピ帳と動画リストが並びます。

鍵は `persistentDataPath/manor-device.json`（`base_url`・`token`・`device_id`・`user_id`・
`paired_at`）に控えられ、次の起動からは番号も出ません。番号は 5 分で切れますが、切れたら
自動で次の番号に変わるので板を見ていれば足ります。

manor が見つからなければ、同梱の見本レシピだけでアプリは動きます。

### 繋ぎ先の決め方

| 順 | 何 | いつ効くか |
|---|---|---|
| 1 | `manor.json` の `base_url` | 明示したとき（tailnet 越しなど、探索が届かない置き方） |
| 2 | 端末の控え（`manor-device.json`）が覚えている口 | 一度でも繋がった後 |
| 3 | **探索**（UDP **8791** へ `manor-discover v1` を投げ、1.5 秒待つ。3回まで） | 同じ Wi-Fi に manor が居るとき |

PC の LAN の住所が変わっても `manor.json` を書き換える必要はありません（探索で見つけ直し、
その口を控えに覚えます）。`manor.json` は **`base_url` の上書きだけ**です。

```json
{ "base_url": "https://manor.<tailnet>.ts.net" }
```

```powershell
adb push manor.json /sdcard/Android/data/com.kazuhiro.kitchenxr/files/manor.json
```

`…/files/` が `Application.persistentDataPath` です（アプリを一度起動しないと無いことがあります）。
読むのは起動のときだけなので、書き換えたら起動し直してください。
**`passcode` は読みません**（v1.0.10 で廃止。書いてあると警告が1行出るだけです）。

### 鍵が失効したら

manor の 設定 → 端末 で「失効」させると、次に叩いたときに 401 が返ります。KitchenXR は
**鍵を捨てて、また 6 桁の番号を出します**——もう一度許可してください。入り直しは自動では
行いません（やり直しとは許可を貰うことなので、板を持っている `Bootstrap` の仕事です）。

## 2. manor 側に要ること

1. Web を LAN に向けて立てる（既定の `127.0.0.1` では Quest から届きません）。

   ```powershell
   uv run manor web serve --host 0.0.0.0 --port 8789
   ```

   `--host` を渡さないときは `[web] host` が既定になります（デスクトップのショートカットから
   立てる場合はこちらを設定してください）。
2. Windows の受信規則を2つ開ける（管理者の PowerShell で1回だけ）。

   ```powershell
   netsh advfirewall firewall add rule name="AI Manor 8789" dir=in action=allow protocol=TCP localport=8789 profile=private
   netsh advfirewall firewall add rule name="AI Manor discover" dir=in action=allow protocol=UDP localport=8791 profile=private
   ```

   UDP 8791 は探索の口です。開けなくても `manor.json` に住所を書けば繋がりますが、開けておくと
   **何も書かずに繋がります**。

LAN に見えている面は「鍵つきの API」と「許可しない限り何も起きないペアリングの口」だけです
（ADR-017 D4）。LAN 上の HTTP は平文のままで、これは残課題です（ADR-017 D7）。
Android 側は平文 HTTP を許可する設定でビルドしています（`AndroidPlayerSetup`。
これが無いと `Insecure connection not allowed` で manor に届きません）。

Tailscale 越しに出すときは `tailscale serve --bg <port>` で HTTPS を張り、
`tailscale serve status` が出す URL を `base_url` に書きます。

## 3. XR が使う API

`Net/ManorClient` が叩くのはこれだけです。認証は `Authorization: Bearer <鍵>`。
すべて例外を投げず `ManorResult<T>`（取れた／繋がらない／断られた）で返します。

| 口 | 何 | 返り |
|---|---|---|
| `POST /api/v1/devices/pair/start` | 番号を貰う（**認証なし**）。`{name, kind:"kitchenxr"}` | `{pair_id, code, expires_in, poll_after}` |
| `POST /api/v1/devices/pair/poll` | 許可されたかを訊く（**認証なし**）。`poll_after` 秒おき | `{status: pending\|approved\|expired, token, device_id, user_id}` |
| `GET /api/v1/kitchen/recipes` | レシピ一覧 | `{"items": [...]}`（下記） |
| `GET /api/v1/kitchen/recipes/{id}` | 契約 JSON（§4）＋ `meta` | |
| `GET /api/v1/kitchen/media` | 動画リスト（XR はこれだけを読む） | `{"items": [...], "updated_at"}` |
| `POST /api/v1/kitchen/cook-sessions` | 調理開始。`{recipe_id}` | `{id, current}` |
| `POST /api/v1/kitchen/cook-sessions/{id}/events` | 進行。`{type: next\|prev\|timer_start\|done, step?}` | `{current, progress}` |
| `GET /api/v1/kitchen/cook-sessions/current` | 途中起動の復帰 | `{id, recipe_id, current}` |
| `POST /api/v1/kitchen/cook-sessions/{id}/end` | 終了（`times_cooked` が増える） | |

`POST /api/v1/auth/login {passcode}` の cookie の経路は口としては残してありますが、
`manor.json` からは通りません（試験と、将来 tailnet 越しに cookie を使う場合のため）。
鍵で叩いて 401 が返ったら鍵を捨ててペアリングし直します。

一覧の各行から読むのは `id`・`title`・`total_minutes`・`kcal`・`hero_image` と、分類
（`category`。無ければ `cuisine`・`main_ingredient` で代える）です。板に並べるのは 20 件まで。
**壊れた行は黙って飛ばし、読めた行だけを返します**——一覧が出ないことで調理が止まらないように。

動画リストの各行は `id`・`title`・`video_id`・`url`・`thumbnail_url`・`author`・`memo`・
`sort_order`（昇順）。`thumbnail_url` が空なら `https://i.ytimg.com/vi/<video_id>/hqdefault.jpg` を
`Domain/MediaJson` が組み立てます。`video_id` は URL をそのまま貼っても取り出しますが、
JS へ埋めるので記号入りの id は通しません。最大 8 件。

## 4. レシピの契約 JSON

この計画で**最も長生きするもの**です。クライアントもサーバもこれだけを見ます。
実例は [`samples/chahan.recipe.json`](samples/chahan.recipe.json)。

```jsonc
{
  "id": 12,
  "title": "鶏むね肉の照り焼き",
  "source_url": "https://…",
  "hero_image": "https://…/hero.jpg",
  "servings": 2,
  "total_minutes": 25,
  "ingredients": [{"name": "鶏むね肉", "qty": "1", "unit": "枚", "prep": "そぎ切り", "group": "主材料"}],
  "tools": ["フライパン"],
  "phases": [{"id": "prep", "title": "下ごしらえ"}],
  "steps": [
    {
      "index": 1, "phase": "prep",
      "title": "肉を切る",
      "instruction": "そぎ切りにして塩こしょう。",
      "image": "https://…/step1.jpg",
      "ingredients_used": ["鶏むね肉"],
      "timer_sec": null,
      "completion": "manual",
      "tips": ["厚さを揃えると火の通りが均一"]
    }
  ],
  "meta": {"kcal": 480, "tags": [], "rating": 4, "memo": "", "favorite": 0, "times_cooked": 3}
}
```

| 項目 | 何 | 決めごと |
|---|---|---|
| `source_url` | 出典 | 表示は「出典」リンクのみ |
| `hero_image` | 完成画像 | URL のまま持つ（保存しない。個人利用） |
| `phases` | 工程を束ねる区切り | 進捗の点の間隔・色に使う |
| `steps[].title` | パネルの見出し | **12 文字以内。短さはサーバで保証する**（クライアントで切り詰めない） |
| `steps[].instruction` | 説明 | **100 文字以内・1〜2 文**（v1.0.12 で 60 → 100。板を左右2列にして置き場が増えたぶん）。文中の `(A)`・`調味料B` などのグループ参照は XR 側が `ingredients[].group` と突き合わせ、**本文は変えずに**下へ `(B)＝しょうゆ 大さじ1・…` の行を添える |
| `steps[].image` | 工程の画像 | 契約上 1:1。無ければ材料名の淡い札で代える |
| `steps[].timer_sec` | タイマーの既定値 | あればその工程で「開始」に入る |
| `steps[].completion` | 進め方 | 今は全部 `manual`。`auto`／`confirm` は認識を入れるときのために列だけ持つ |
| `steps[].tips` | 補足 | 折りたたみ。既定は隠す |
| `meta` | 栄養・タグ・評価・メモ・作った回数 | manor 側の「うちの値」。レシピ本体の差し替えでは消えない |

分量の単位は自由文字列（「大さじ2」）。正規化はしません。

## 5. 端末内のファイルとログ

`/sdcard/Android/data/com.kazuhiro.kitchenxr/files/` の下（`Application.persistentDataPath`）:

| ファイル | 何 |
|---|---|
| `manor.json` | 繋ぎ先の上書き（任意） |
| `manor-device.json` | 端末の鍵の控え |
| `recipes/index.json`・`recipes/<id>/recipe.json`・画像 | レシピ一覧とレシピの写し |
| `media.json`・`media.bundled.json` | 動画リストの写しと、同梱を比べるための控え |
| `media-thumbs/<video_id>.jpg` | 再生リストのサムネイル |
| `cook_events.jsonl` | 進行の待ち行列 |
| `last_session.json`・`settings.json` | 最後の調理と、文字・板の大きさ |
| `anchors.json`・`panels.json` | アンカーの鍵と、板の位置の控え |
| `logs/kitchenxr.log` | 端末内のログ（1MB で `kitchenxr.1.log`・`.2.log` へ送って3世代） |

- ログの取り方: `adb pull /sdcard/Android/data/com.kazuhiro.kitchenxr/files/logs/ .`
  または MQDH のファイル一覧。USB で繋いでいるときは `adb logcat -s Unity` に同じものが流れます。
- 一覧の板の札で繋がり方が分かります。
  - 何も出ていない → manor から一覧を取れている
  - 「manor が見つかりません（見本だけ）」 → 探索も控えも `manor.json` も空振り（§2 へ）
  - 「manor と繋いでいません（見本だけ）」 → 場所は分かっているが、まだ許可されていない
  - 「manor に繋がりません（控えた一覧）」 → 前に取った一覧を出している（圏外・manor が止まっている）
  - 「ペアリングできません（…）」 → 番号を貰う口が断った（manor 側のログを見る）
- 動画の板の札3行には、窓の矩形と絵の実寸・板が最後にしたこと・最後の失敗が出ます。
- 板の覚えた位置を捨てたいときは `anchors.json`・`panels.json` を消します
  （`adb shell run-as com.kazuhiro.kitchenxr` か、アプリのデータ消去）。

> Quest は台所で電子レンジを使うと Wi-Fi が切れます。KitchenXR はそれを前提にしていて、
> 一覧・レシピ・画像は取れたときに手元へ写し、以後はそこから出します。工程の進みは待ち行列に
> 積まれ、繋がったときにまとめて送られます。**送れないことで調理は止まりません。**
</content>
