# manor への繋ぎ方（端末のペアリング）

KitchenXR は AI Manor の**料理長のレシピ帳**（manor の ADR-015）と**動画リスト**（ADR-016）を
読む側です。v1.0.10 から、繋ぎ方は**端末のペアリング**になりました（正は manor の
`docs/design/ADR-017_device_pairing.md`）。合言葉を平文でファイルに置くのはやめ、
**端末ごとの鍵**を主人が Web で許可して渡します。

## 1. やること（これだけ）

1. PC で manor を家の Wi-Fi に向けて立てる（第4節）。
2. Quest で KitchenXR を起動する。一覧の板に**6桁の番号**が大きく出ます。
3. PC のブラウザで manor を開き、**設定 → 端末**でその番号を入れ、利用者を選んで「許可」。
4. 板の番号が消えて、レシピ帳と動画リストが並びます。

**これで終わりです。** 端末の鍵は Quest の中
（`Android/data/com.kazuhiro.kitchenxr/files/manor-device.json`）に控えられ、次の起動からは
番号も出ません。板に文字入力は置かないので（設計 `Docs/design/PROTOTYPE.md` §6）、
**番号は端末に出し、主人が Web に入れる**——向きが逆なのがこの方式の要点です。

manor が見つからなければ一覧の板に「manor が見つかりません（見本だけ）」の札が出て、
見本の炒飯（アプリに同梱）だけが並びます——**何も置かなくてもアプリは動きます。**

## 2. どこに繋ぐかは、ふつう自分で決まります

KitchenXR は起動時に順にこう決めます:

| 順 | 何 | いつ効くか |
|---|---|---|
| 1 | `manor.json` の `base_url` | 主人が**明示した**とき（第5節。tailnet 越しなど） |
| 2 | 端末の控え（`manor-device.json`）が覚えている口 | 一度でも繋がった後 |
| 3 | **探索**（UDP 8791 へ `manor-discover v1` を投げ、1.5 秒待つ。3回まで） | 同じ Wi-Fi に manor が居るとき |

つまり PC の LAN の住所が変わっても、`manor.json` を書き換える必要はありません
（探索で見つけ直し、その口を控えに覚えます）。

## 3. 鍵が失効したら

manor の 設定 → 端末 で「失効」させると、次に叩いたときに 401 が返ります。KitchenXR は
**鍵を捨てて、また6桁の番号を出します**——もう一度 設定 → 端末 で許可してください。
番号は5分で切れますが、切れたら自動で次の番号に変わるので、板をそのまま見ていれば大丈夫です。

## 4. 同じ家の Wi-Fi で繋ぐ（一番短い道）

1. manor を LAN に向けて起動する（既定の `127.0.0.1` では Quest から届きません）:

   ```powershell
   uv run manor web serve --host 0.0.0.0 --port 8789
   ```

   LAN に見えるのは「鍵つきの API」と「主人が許可しない限り何も起きないペアリングの口」だけです
   （manor の ADR-017 D4）。
2. Windows の受信規則を2つ開ける（管理者の PowerShell で1回だけ）:

   ```powershell
   netsh advfirewall firewall add rule name="AI Manor 8789" dir=in action=allow protocol=TCP localport=8789 profile=private
   netsh advfirewall firewall add rule name="AI Manor discover" dir=in action=allow protocol=UDP localport=8791 profile=private
   ```

   UDP 8791 は探索（第2節）の口です。開けなくても `manor.json` に住所を書けば繋がりますが、
   開けておくと**何も書かずに繋がります**。
3. Quest で KitchenXR を起動し、出た番号を manor の 設定 → 端末 で許可する。

> Quest は台所で電子レンジを使うと Wi-Fi が切れます。KitchenXR はそれを前提にしていて、
> 一覧・レシピ・画像は**取れたときに手元へ写して、以後はそこから出します**。
> 工程の進み（次へ／戻る／作り終えた）は追記ファイルの待ち行列に積まれ、
> 繋がったときにまとめて送られます。**送れないことで調理は止まりません。**

## 5. `manor.json`（繋ぎ先の上書き。ふつうは要りません）

探索が届かない置き方——**tailnet 越し**や、別の網から繋ぐとき——だけ、繋ぎ先を明示します。

```json
{ "base_url": "https://manor.<あなたのtailnet>.ts.net" }
```

| 鍵 | 何 |
|---|---|
| `base_url` | manor の Web の根。末尾の `/` は付けても付けなくても構いません |

**`passcode` はもう読みません**（v1.0.10 で廃止）。書いてあると起動時に
「v1.0.10 で廃止。ペアリングへ」の警告が1行出るだけです——行は消して構いません。

Quest へ置くには USB で繋ぎ、PC から:

```powershell
adb push manor.json /sdcard/Android/data/com.kazuhiro.kitchenxr/files/manor.json
```

`…/files/` が `Application.persistentDataPath` です。アプリを一度起動してからでないと
このフォルダが無いことがあります（無ければ `adb shell mkdir -p …/files` で作ってください）。
書き換えたら**アプリを起動し直してください**（読むのは起動のときだけです）。

### Tailscale 越しに出す

```powershell
manor web serve            # 既定の口で立てる
tailscale serve --bg 8765  # HTTPS で tailnet に出す（口の番号は manor の設定に合わせる）
```

`tailscale serve status` が出す `https://<マシン名>.<tailnet>.ts.net` をそのまま `base_url` に
書きます。証明書は Tailscale が用意するので、Quest 側で何かを入れる必要はありません。

## 6. 繋がっているかを見る

- 一覧の板の上の小さな札:
  - 何も出ていない → manor から一覧を取れています
  - 「manor が見つかりません（見本だけ）」 → 探索も控えも `manor.json` も空振り（第4節へ）
  - 「manor と繋いでいません（見本だけ）」 → 場所は分かっているが、まだ許可されていない
  - 「manor に繋がりません（控えた一覧）」 → 前に取った一覧を出しています（圏外・manor が寝ている）
  - 「ペアリングできません（…）」 → 番号を貰う口が断りました（manor 側のログを見てください）
- 細かい理由は `adb logcat -s Unity | Select-String KitchenXR` に出ます
  （ペアリングの番号もここに出ます）

## 7. 鍵を持たせておいてよいのか

端末の鍵はアプリ専用の領域（`Android/data/com.kazuhiro.kitchenxr/files/`）に置きます。
manor が持っているのは**その鍵のハッシュだけ**（合言葉と同じ扱い。ADR-017 D1）で、
鍵で触れるのは `/api/v1/kitchen/*` と `/api/v1/devices/me` に限られます。
取り上げたいときは manor の 設定 → 端末 で「失効」——その瞬間から通りません。

LAN 上の HTTP は平文のままです（ADR-017 D7 に残した課題。Wi-Fi は無線区間では
暗号化されているので、家の中ではこれを受け入れています）。

## 動画リスト（ADR-016）

鍵があれば、起動時にレシピ帳と同じ経路で `GET /api/v1/kitchen/media` を読み、
`persistentDataPath/media.json` へ写します。一覧の編集は manor の Web（料理長 → 動画リスト）。
圏外なら写しを読み、まだ許可されていなければ同梱の見本（`StreamingAssets/media.json`）です。
