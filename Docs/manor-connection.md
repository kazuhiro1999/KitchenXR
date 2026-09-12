# manor への繋ぎ方（`manor.json`）

KitchenXR は AI Manor の**料理長のレシピ帳**（manor の ADR-015）を読む側です。
繋ぎ先と合言葉は、板ではなく**ファイルで**渡します——Quest の空中キーボードで URL と
合言葉を打つのは調理の前にやりたいことではないし、打ち間違いが「繋がらない」に化けるからです
（設計 `Docs/design/PROTOTYPE.md` §6「文字入力はパネルに置かない」）。

**置かなくてもアプリは動きます。** その場合は一覧の板に「manor 未設定（見本だけ）」の札が出て、
見本の炒飯（アプリに同梱）だけが並びます。

## 1. ファイルの中身

```json
{
  "base_url": "https://manor.<あなたのtailnet>.ts.net",
  "passcode": "manor の合言葉"
}
```

| 鍵 | 何 |
|---|---|
| `base_url` | manor の Web の根。末尾の `/` は付けても付けなくても構いません |
| `passcode` | manor の合言葉（`POST /api/v1/auth/login` に渡すもの）。**manor の設定と同じもの**。manor を loopback で動かしている場合は空でも構いません |

## 2. Quest 3 へ置く

USB で繋ぎ、PC から:

```powershell
adb push manor.json /sdcard/Android/data/com.kazuhiro.kitchenxr/files/manor.json
```

`…/files/` が `Application.persistentDataPath` です。アプリを一度起動してからでないと
このフォルダが無いことがあります（無ければ `adb shell mkdir -p …/files` で作ってください）。

書き換えたら**アプリを起動し直してください**（読むのは起動のときだけです）。

確かめる:

```powershell
adb shell cat /sdcard/Android/data/com.kazuhiro.kitchenxr/files/manor.json
```

## 3. `base_url` に何を書くか

### (a) Tailscale 越し（家の外からでも繋がる。おすすめ）

PC 側で manor を出しておきます:

```powershell
manor web serve            # 既定の口で立てる
tailscale serve --bg 8765  # HTTPS で tailnet に出す（口の番号は manor の設定に合わせる）
```

`tailscale serve status` が出す `https://<マシン名>.<tailnet>.ts.net` をそのまま `base_url` に
書きます。証明書は Tailscale が用意するので、Quest 側で何かを入れる必要はありません。

### (b) 家の LAN だけ（同じ Wi-Fi にいるとき）

```powershell
manor web serve --host 192.168.0.2     # PC の LAN の住所
```

`base_url` は `http://192.168.0.2:8765`（番号は manor の口に合わせる）。
**平文の HTTP なので家の中だけにしてください。**

> Quest は台所で電子レンジを使うと Wi-Fi が切れます。KitchenXR はそれを前提にしていて、
> 一覧・レシピ・画像は**取れたときに手元へ写して、以後はそこから出します**。
> 工程の進み（次へ／戻る／作り終えた）は追記ファイルの待ち行列に積まれ、
> 繋がったときにまとめて送られます。**送れないことで調理は止まりません。**

## 4. 繋がっているかを見る

- 一覧の板の上の小さな札:
  - 何も出ていない → manor から一覧を取れています
  - 「manor 未設定（見本だけ）」 → `manor.json` が無い／読めない
  - 「manor に繋がりません（控えた一覧）」 → 前に取った一覧を出しています
  - 「合言葉が違います」 → `passcode` が manor の設定と違います
- 細かい理由は `adb logcat -s Unity | Select-String KitchenXR` に出ます

## 5. 合言葉の寿命

manor の cookie（`manor_session`）は 24 時間で切れます。KitchenXR は
**401 が返ったら1度だけ入り直して、同じ頼みを送り直します**——前の晩に使ったまま
翌朝また使っても、主人が何かする必要はありません。
2度目も断られたときだけ「合言葉が違います」の札が出ます。

## 6. 置いてよいのか（合言葉を平文で持つこと）

`manor.json` はアプリ専用の領域（`Android/data/com.kazuhiro.kitchenxr/files/`）に置きます。
manor 側の合言葉は「家庭内の1台を守るためのもの」（manor の ADR-005 §2 D4）なので、
同じ強さのものを同じ家の中の別の1台に置く、という整理です。
外に出す前提のものではありません。

## 動画リスト（ADR-016）

`manor.json` があれば、起動時にレシピ帳と同じ合言葉で `GET /api/v1/kitchen/media` を読み、
`persistentDataPath/media.json` へ写す。一覧の編集は manor の Web（料理長 → 動画リスト）。
圏外なら写しを読む。manor 未設定なら同梱の見本（`StreamingAssets/media.json`）。
