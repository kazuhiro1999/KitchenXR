# 動画の一覧（`media.json`）の書き方

**2026-09-13 改め（manor ADR-016）: 一覧の正は manor の「動画リスト」**（料理長の Web の別ページ。
URL を貼ると題名とサムネイルを取って登録できる）。XR は起動時に `GET /api/v1/kitchen/media` で取り、
`persistentDataPath/media.json` へ写して、圏外ならその写しを読む。以下の `media.json` の話は
**manor 未設定のときの見本**と、写しの置き場の説明として残す。


動画の板に出る題名の列は **クライアントのローカル設定**（設計 `design/PROTOTYPE.md` §2・§6）。
manor には置かない。形は下のとおりで、`title`（板に出す題名）と `video_id`（YouTube の動画 id）
だけ。**最大 8 件**まで読み、9 件目以降と読めない行は黙って飛ばす（1 行のタイプミスで板が丸ごと
死なないため）。`video_id` には `https://www.youtube.com/watch?v=…`・`https://youtu.be/…`・
`…/shorts/…` の URL をそのまま貼ってもよい（id を取り出す）。文字入力は板に置かない設計なので、
一覧を変える手段はこのファイルだけ。

```jsonc
[
  { "title": "今日のショーツ", "video_id": "dQw4w9WgXcQ" },
  { "title": "料理中に流すドラマ", "video_id": "https://www.youtube.com/watch?v=YbJOTdZBX1g" }
]
```

**置き場と書き換え方。** アプリに同梱している見本は `Assets/StreamingAssets/media.json`。
起動時、端末に一覧がまだ無ければこの見本を
`Application.persistentDataPath/media.json`
（Quest 3 では `/sdcard/Android/data/com.kazuhiro.kitchenxr/files/media.json`）へ写し、
**以後はそちらだけを読む**。つまりアプリを入れ直さずに一覧を変えられる——PC から

```
adb push media.json /sdcard/Android/data/com.kazuhiro.kitchenxr/files/media.json
```

で置き換え、アプリを立ち上げ直せば新しい一覧が出る（`adb pull` で今の中身を取れる）。
同梱の見本のほうを直したときは、端末側のファイルを一度消さないと反映されない。

**線引き。** DRM 付きの配信（Netflix・Prime 等）は WebView では再生できない
（Widevine L1 が無い。設計 §6）。YouTube・ショーツ・一般の Web 動画は出る。

**動かないときの見方（2026-09-13）。** 板の下、ボタンの上に**札が3行**出る。
1 行目は中の様子（`WebView INITIALIZED / HTML 読込済 / 動画 待機（cue） <id> / 窓 …→ 絵 …mm`）、
2 行目は板が最後にしたこと（「一覧: 「…」→ 読み込み」「再生を頼みました」等）と、主人の
`YoutubePlayer` が Unity のログに出した最後の一言、3 行目（赤）は最後の失敗。
「再生」は選ぶ前でも押せる（`youtube.html` が cue している既定の動画が始まる）。
再生を頼んで約 1 秒しても YouTube が「再生中」と返さないときは、板が WebView の中央を一度
タップする（Android の WebView は人の操作を伴わない再生を拒むことがあるため。1 行目の末尾に
「タップ n」と出る）。それでも始まらないときは、この 3 行を読み上げていただければ絞り込める。

**同梱を直して APK を入れ直したとき（2026-09-13 改め）。** 起動のたびに同梱も読み、前回写した控え
（`media.bundled.json`、`media.json` と同じ場所）と違えば「APK 側が変わった」と見なす。端末側の
`media.json` が控えと同じ（誰も直していない）なら新しい同梱で置き換え、違う（`adb push` で直した）
なら端末側を残す。両方直したときは端末側が勝つので、同梱の変更を反映したければ端末側の
`media.json` を一度消す。
