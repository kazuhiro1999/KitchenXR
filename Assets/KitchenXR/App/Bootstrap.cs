using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using KitchenXR.Net;
using KitchenXR.Platform;
using KitchenXR.Platform.ArFoundation;
using KitchenXR.Platform.MetaCamera;
using KitchenXR.Platform.Null;
using KitchenXR.Presentation;
using KitchenXR.Presentation.Video;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.UIElements;

namespace KitchenXR.App
{
    /// <summary>
    /// どのアダプタを挿すかはここだけ。起動 → 待ち行列を流す → 途中の調理があれば復帰 →
    /// 無ければレシピを選ぶ板 → 選んだら JSON と画像を先に全部手元へ → 調理の3枚へ。
    ///
    /// 表示は常にローカルの写しから出し、サーバへの報せは <see cref="CookEventQueue"/> に
    /// 積むだけ（送れないことで調理を止めない）。manor.json が無ければ見本だけで動く。
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        [Header("見本レシピ（Resources 配下。manor 未設定でもこれだけは開ける）")]
        [SerializeField] private string _recipeResourcePath = "Recipes/chahan";

        [Header("パネル（Kitchen.unity で配置済みのものを挿す）")]
        [SerializeField] private RecipeListPanel _recipeListPanel;
        [SerializeField] private RecipePanel _recipePanel;
        [SerializeField] private IngredientsPanel _ingredientsPanel;
        [SerializeField] private TimerPanel _timerPanel;
        [SerializeField] private VideoPanel _videoPanel;
        [SerializeField] private CookingModeInputGate _cookingModeInputGate;

        [Header("配置モード（操作は全部 手元のメニュー側）")]
        [SerializeField] private PanelPlacement _panelPlacement;
        [SerializeField] private PlacementMenuPanel _placementMenuPanel;

        /// <summary>
        /// 手首の釦。メニューの出し入れを持つ。配置モードへ入るときは必ず開ける——
        /// メニューが閉じたままだと「保存」「やめる」に手が届かず、出られなくなる。
        /// </summary>
        [SerializeField] private WristMenu _wristMenu;

        [Header("初期配置（頭の前0.8m・目線より少し下に3枚）")]
        [SerializeField] private Transform _headTransform; // 未指定なら Camera.main を使う
        [SerializeField] private float _forwardDistanceMeters = 0.8f;
        [SerializeField] private float _belowEyelineMeters = 0.08f;
        [SerializeField] private float _lateralSpacingMeters = 0.5f;

        // AR Foundation が使えるなら ArAnchorStore が挿さる。
        private IAnchorStore _anchorStore;
        private IPassthroughControl _passthrough;
        private IHandInputPolicy _handInputPolicy;

        /// <summary>板の位置の控え（`panels.json`）。アンカーが使えるときも必ず書く（退避路）。</summary>
        private PanelPoseFile _panelPoseFile;

        /// <summary>見え方の設定（文字と板の大きさ）。</summary>
        private DisplaySettings _displaySettings;

        private RecipeStore _recipeStore;
        private MediaStore _mediaStore;
        private ManorClient _manor;
        private CookEventQueue _eventQueue;
        private LastSessionStore _lastSessionStore;

        /// <summary>端末の鍵の控え（`manor-device.json`）。</summary>
        private ManorDeviceFile _deviceFile;

        /// <summary>
        /// manor に繋げないときに一覧の札へ出す理由（「manor が見つかりません（見本だけ）」など）。
        /// 繋がっているときは空。
        /// </summary>
        private string _manorStatus = string.Empty;

        /// <summary>今ペアリングの最中か（番号を二重に取らない・覆いを取り合わない）。</summary>
        private bool _pairing;

        private CookSession _session;

        /// <summary>カメラの下見（v1-d）。動画の板の「カメラ」の釦が使う。Editor では受け皿に落ちる。</summary>
        private CameraProbe _cameraProbe;

        /// <summary>今の調理の manor 側のセッション id。見本・manor 未設定のときは null。</summary>
        private int? _manorSessionId;

        /// <summary>見本（Resources）の中身。一覧の先頭の行と、開いたときの写し元。</summary>
        private string _bundledJson;
        private RecipeSummary _bundledSummary;

        private CancellationTokenSource _cts;

        /// <summary>
        /// カメラの権限は**起動時**（場面が読まれる前＝AR セッションが立つ前）に求める。
        ///
        /// 後から許しても、そのセッションの subsystem は 1 枚も返しません。前は許可の直後に
        /// <c>ARCameraManager</c> を起こし直していましたが、Unity OpenXR: Meta では
        /// パススルーの映像そのものがそこにぶら下がっているので、**MR の背景が真っ暗に
        /// なって戻りませんでした**（実測 2026-09-13。調査 §7）。
        ///
        /// ここで許してもらえれば、次の起動——多くは初回に許した時点の起動——から
        /// 最初のセッションでカメラが動きます。Camera Image Support を立てていないビルド
        /// （manifest に権限が無い）では何もしない。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RequestHeadsetCameraPermissionAtStartup()
        {
            const string permission = MetaOpenXRPassthroughCamera.HeadsetCameraPermission;

            if (!MetaOpenXRPassthroughCamera.IsHeadsetCameraDeclared() ||
                Permission.HasUserAuthorizedPermission(permission))
            {
                return;
            }

            Debug.Log("[KitchenXR] カメラ: 起動時に権限を求めます（AR セッションが立つ前）。");
            Permission.RequestUserPermission(permission);
        }

        private void Awake()
        {
            // AR Foundation（ARAnchorManager）が居れば ArAnchorStore、居なければ InMemory。
            // どちらでも控え（panels.json）へは必ず書くので、板の位置は失われない。
            _anchorStore = AnchorStoreFactory.Create();
            _panelPoseFile = PanelPoseFile.CreateDefault();

            _passthrough = new NullPassthrough();
            _passthrough.Enable(); // MR テンプレートは既定でパススルー済みだが、状態としても明示しておく。
            _handInputPolicy = new DefaultHandInputPolicy();

            _recipeStore = RecipeStore.CreateDefault();
            _manor = ManorClient.CreateDefault();
            _deviceFile = ManorDeviceFile.CreateDefault();

            // 鍵が失効したら（manor が 401 を返したら）ペアリングをやり直す。
            _manor.DeviceRevoked += HandleDeviceRevoked;

            _eventQueue = CookEventQueue.CreateDefault();
            _lastSessionStore = LastSessionStore.CreateDefault();

            // 見え方は板を立てるより先に読む（壊れていれば黙って既定に戻る）。当てるのは Start。
            _displaySettings = DisplaySettings.CreateDefault();
            _displaySettings.Load();

            LoadBundledSample();

            if (_recipePanel != null)
            {
                _recipePanel.NextRequested += HandleNext;
                _recipePanel.PrevRequested += HandlePrev;
                _recipePanel.BackToListRequested += HandleBackToList;
                _recipePanel.FinishRequested += HandleFinish;
            }

            if (_recipeListPanel != null)
            {
                _recipeListPanel.RecipeSelected += HandleRecipeSelected;
                _recipeListPanel.PlacementRequested += HandlePlacementRequested;
                _recipeListPanel.FontScaleSelected += HandleFontScaleSelected;
                _recipeListPanel.PanelScaleSelected += HandlePanelScaleSelected;

                // 一覧の写真（hero）も工程の画像と同じ経路でローカルから出す。
                _recipeListPanel.Bind(_recipeStore);
            }

            if (_recipePanel != null)
            {
                _recipePanel.PlacementRequested += HandlePlacementRequested;
            }

            if (_placementMenuPanel != null)
            {
                _placementMenuPanel.PlacementRequested += HandlePlacementRequested;
                _placementMenuPanel.SaveRequested += HandlePlacementSave;
                _placementMenuPanel.UndoRequested += HandlePlacementUndo;
                _placementMenuPanel.CancelRequested += HandlePlacementCancel;
                _placementMenuPanel.RecallRequested += HandlePlacementRecall;
            }

            if (_cookingModeInputGate != null)
            {
                _cookingModeInputGate.Bind(_handInputPolicy);
            }

            SetUpPlacement();

            // カメラの下見（v1-d）。実機は Meta の口、Editor は受け皿（札に理由が出る）。
            // 板が無くても作る——manor への1枚の投げもここが持っているので、口は1つにしておく。
            _cameraProbe = new CameraProbe(PassthroughCameraFactory.Create(), _manor);
            _videoPanel?.BindCamera(_cameraProbe);

            // 起動の見た目は「レシピを選ぶ板」。調理の3枚は選んでから出す。
            ShowListMode();
        }

        private void Start()
        {
            // 手のひらメニューは「配置」だけを出した状態から始める（配置モードの外）。
            _placementMenuPanel?.SetPlacing(false);

            // 板の縮尺は控え（panels.json）にもアンカーにも入っていない（位置と向きだけ）ので、
            // 起動のたびにここで当て直す。文字のクラスも板の root が出来てからでないと付かない。
            ApplyDisplaySettings();

            PlaceInitialPanels();

            _cts = new CancellationTokenSource();

            // 覚えている場所へ戻す（アンカー → 控え → 既定）。
            // 起動の道筋（一覧・復帰）とは独立に走らせる——板の位置は中身より先に決まってよい。
            RestorePlacementAsync(_cts.Token).Forget();

            // 動画の一覧は manor に繋ぎ終えてから（StartupAsync の中で）始める——
            // 鍵が決まる前に走らせると、繋がっているのに手元の写しのままになる。
            StartupAsync(_cts.Token).Forget();
        }

        private void OnDestroy()
        {
            if (_recipePanel != null)
            {
                _recipePanel.NextRequested -= HandleNext;
                _recipePanel.PrevRequested -= HandlePrev;
                _recipePanel.BackToListRequested -= HandleBackToList;
                _recipePanel.FinishRequested -= HandleFinish;
            }

            if (_recipeListPanel != null)
            {
                _recipeListPanel.RecipeSelected -= HandleRecipeSelected;
                _recipeListPanel.PlacementRequested -= HandlePlacementRequested;
                _recipeListPanel.FontScaleSelected -= HandleFontScaleSelected;
                _recipeListPanel.PanelScaleSelected -= HandlePanelScaleSelected;
            }

            if (_recipePanel != null)
            {
                _recipePanel.PlacementRequested -= HandlePlacementRequested;
            }

            if (_placementMenuPanel != null)
            {
                _placementMenuPanel.PlacementRequested -= HandlePlacementRequested;
                _placementMenuPanel.SaveRequested -= HandlePlacementSave;
                _placementMenuPanel.UndoRequested -= HandlePlacementUndo;
                _placementMenuPanel.CancelRequested -= HandlePlacementCancel;
                _placementMenuPanel.RecallRequested -= HandlePlacementRecall;
            }

            if (_panelPlacement != null)
            {
                _panelPlacement.PlacementFinished -= HandlePlacementFinished;
            }

            if (_manor != null)
            {
                _manor.DeviceRevoked -= HandleDeviceRevoked;
            }

            _cameraProbe?.Dispose();

            _cts?.Cancel();
            _cts?.Dispose();
        }

        // ---------------------------------------------------------------- 起動の道筋

        /// <summary>
        /// 起動して最初にやること。順番に意味がある:
        ///   1. manor の場所と鍵を決める  2. 動画リストを読む  3. 溜まっている進行の記録を流す
        ///   4. 途中の調理があれば一覧を飛ばして続きから  5. 無ければ一覧  6. 鍵が無ければペアリング
        ///
        /// ペアリングを一覧の後に置いているのは、番号の板が一覧の覆いなので、
        /// 台所に来るまでの間も動画の板と見本のレシピが揃っているようにするため。
        /// </summary>
        private async UniTaskVoid StartupAsync(CancellationToken token)
        {
            await ConnectManorAsync(token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            // 場所と鍵が決まってから動画リスト（先に走らせると、繋がっているのに手元の写しのままになる）。
            LoadMediaAsync(token).Forget();

            if (_manor.IsConfigured)
            {
                await _eventQueue.FlushAsync(_manor, token);
            }

            if (await TryResumeAsync(token))
            {
                // 調理の続きが出ている。ペアリングはしない——番号の板は一覧の覆いなので、
                // 今出しても見えないし、許可されたら一覧へ戻してしまう。
                return;
            }

            await RefreshListAsync(token);

            // 場所は分かっているのに鍵が無い＝まだ許可されていない端末（初回・失効のあと）。
            if (_manor.HasBaseUrl && !_manor.HasDeviceToken && await PairAsync(token))
            {
                LoadMediaAsync(token).Forget();
                await RefreshListAsync(token);
            }
        }

        private string _discoveryFailure = string.Empty;

        /// <summary>
        /// manor の場所と鍵を決める。繋ぎ先は `manor.json` の `base_url` → 端末の控え →
        /// 探索（UDP 8791）、鍵は端末の控え（無ければ <see cref="PairAsync"/> が貰いに行く）。
        ///
        /// `manor.json` を控えより先に見るのは、探索が届かない置き方（tailnet 越し・ポート変更）を
        /// 明示した指定のほうが正しいから。どれも決まらなければ札を出して見本だけで動く。
        /// </summary>
        private async UniTask ConnectManorAsync(CancellationToken token)
        {
            var device = _deviceFile.Load();

            if (_manor.Settings.IsConfigured)
            {
                Debug.Log($"[KitchenXR] manor の場所は {ManorSettings.FileName} の指定です: {_manor.Settings.BaseUrl}");
            }
            else if (device != null && device.HasBaseUrl)
            {
                _manor.UseBaseUrl(device.BaseUrl, "控え");
            }
            else
            {
                _recipeListPanel?.ShowBusy("manor を探しています");
                var discovery = new ManorDiscovery();
                var found = await discovery.FindBaseUrlAsync(token: token);
                if (!string.IsNullOrEmpty(found))
                {
                    _manor.UseBaseUrl(found, "探索");
                }
                else
                {
                    _discoveryFailure = discovery.LastFailure;
                }
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            if (!_manor.HasBaseUrl)
            {
                // 理由（探索の失敗の言葉）を札に添える——実機で読めば切り分けられる。
                _manorStatus = string.IsNullOrEmpty(_discoveryFailure)
                    ? "manor が見つかりません（見本だけ）"
                    : $"manor が見つかりません（見本だけ）— 探索: {_discoveryFailure}";
                _recipeListPanel?.HideBusy();
                return;
            }

            // 見つけた口（または控えの口）を覚える。次の起動は探索を飛ばせる。
            _deviceFile.SaveBaseUrl(_manor.Settings.BaseUrl);

            _recipeListPanel?.HideBusy();

            if (device != null && device.HasToken)
            {
                _manor.UseDeviceToken(device.Token);
                _manorStatus = string.Empty;
                return;
            }

            // 鍵が無い。番号を貰うのは一覧を出した後（<see cref="StartupAsync"/>）。
            _manorStatus = "manor と繋いでいません（見本だけ）";
        }

        /// <summary>
        /// ペアリング。番号を貰って板に大きく出し、manor の Web の 設定 → 端末 で許可されるのを
        /// `poll_after` 秒おきに訊く。番号は5分で失効するので `expired` が返ったら取り直す
        /// （何度切れても板にはいつも生きた番号が出ている）。繋がらなくなったら止める。
        /// </summary>
        private async UniTask<bool> PairAsync(CancellationToken token)
        {
            if (_pairing)
            {
                return false;
            }

            _pairing = true;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var started = await _manor.PairStartAsync(DeviceName(), ManorClient.DeviceKind, token);
                    if (!started.IsSuccess)
                    {
                        _manorStatus = started.IsOffline
                            ? "manor に繋がりません（見本だけ）"
                            : $"ペアリングできません（{started.Message}）";
                        _recipeListPanel?.HideBusy();
                        return false;
                    }

                    _recipeListPanel?.ShowPairing(started.Value.Code);
                    Debug.Log($"[KitchenXR] ペアリングの番号: {started.Value.Code}"
                              + "（manor の 設定 → 端末 で許可してください）");

                    var outcome = await PollUntilApprovedAsync(started.Value, token);
                    if (outcome == PairOutcome.Approved)
                    {
                        return true;
                    }

                    if (outcome != PairOutcome.Expired)
                    {
                        return false; // 繋がらなくなった・断たれた（札に理由が入っている）。
                    }

                    // 番号が失効しただけ（5 分）。次の番号を取り直す。
                }

                return false;
            }
            finally
            {
                _pairing = false;
            }
        }

        /// <summary>1つの番号の行き先。</summary>
        private enum PairOutcome
        {
            /// <summary>許可された（鍵を控えた）。</summary>
            Approved,

            /// <summary>番号が死んだ。取り直せばよい。</summary>
            Expired,

            /// <summary>繋がらない・断られた・アプリが終わる。やめる。</summary>
            Failed,
        }

        /// <summary>
        /// 1つの番号について許可を待つ。`approved` なら鍵を控える。
        /// `expired` なら呼び出し側が番号を取り直す。
        /// </summary>
        private async UniTask<PairOutcome> PollUntilApprovedAsync(PairStart pair, CancellationToken token)
        {
            var interval = Mathf.Clamp(pair.PollAfterSeconds, 1, 30);
            var attempts = Mathf.Max(1, pair.ExpiresInSeconds / interval);

            for (var i = 0; i < attempts; i++)
            {
                await UniTask.Delay(interval * 1000, ignoreTimeScale: true, cancellationToken: token)
                    .SuppressCancellationThrow();

                if (token.IsCancellationRequested)
                {
                    return PairOutcome.Failed;
                }

                var polled = await _manor.PairPollAsync(pair.PairId, token);
                if (!polled.IsSuccess)
                {
                    _manorStatus = polled.IsOffline
                        ? "manor に繋がりません（見本だけ）"
                        : $"ペアリングできません（{polled.Message}）";
                    _recipeListPanel?.HideBusy();
                    return PairOutcome.Failed;
                }

                if (polled.Value.IsApproved)
                {
                    // 鍵は一度しか返らない。受けた順に控えてから当てる。
                    _deviceFile.Save(
                        _manor.Settings.BaseUrl, polled.Value.Token,
                        polled.Value.DeviceId, polled.Value.UserId);
                    _manor.UseDeviceToken(polled.Value.Token);

                    _manorStatus = string.Empty;
                    _recipeListPanel?.HideBusy();
                    Debug.Log("[KitchenXR] 端末が許可されました（鍵を控えました）。");
                    return PairOutcome.Approved;
                }

                if (polled.Value.IsExpired)
                {
                    return PairOutcome.Expired; // 5 分が過ぎた。新しい番号を取り直す。
                }
            }

            return PairOutcome.Expired;
        }

        /// <summary>
        /// manor の 設定 → 端末 の一覧に並ぶ名前。機種を添えるのは、家に2台あるときの見分け。
        /// </summary>
        private static string DeviceName()
        {
            var model = SystemInfo.deviceModel;
            return string.IsNullOrEmpty(model) || model == SystemInfo.unsupportedIdentifier
                ? "Quest 3"
                : $"Quest 3 ({model})";
        }

        /// <summary>
        /// 鍵が通らなくなった（401＝Web で失効させた）。控えから鍵を消して
        /// ペアリングからやり直す。繋ぎ先は残す。
        /// </summary>
        private void HandleDeviceRevoked()
        {
            _deviceFile?.ForgetToken();

            if (_cts == null || _pairing)
            {
                return;
            }

            RepairAsync(_cts.Token).Forget();
        }

        /// <summary>
        /// やり直したペアリングが通ったら一覧を読み直す。
        /// 調理の最中なら一覧へ戻さない——工程の進みは待ち行列に積まれていて失われないので、
        /// 鍵の入れ替えのために板を取り替えるほうが悪い。番号は一覧の覆いに出たまま残る。
        /// </summary>
        private async UniTaskVoid RepairAsync(CancellationToken token)
        {
            var paired = await PairAsync(token);
            if (token.IsCancellationRequested || _session != null)
            {
                return;
            }

            if (paired)
            {
                await RefreshListAsync(token);
            }
            else
            {
                _recipeListPanel?.SetStatus(_manorStatus);
            }
        }

        /// <summary>
        /// 途中起動の復帰。manor に未終了のセッションがあればそれを、繋がらなければ手元の控えを使う。
        /// </summary>
        private async UniTask<bool> TryResumeAsync(CancellationToken token)
        {
            if (_manor.IsConfigured)
            {
                var current = await _manor.CurrentSessionAsync(token);
                if (current.IsSuccess && current.Value.Exists)
                {
                    var known = FindSummary(current.Value.RecipeId);
                    var summary = known ?? new RecipeSummary(
                        current.Value.RecipeId, current.Value.RecipeId, 0, string.Empty, null);

                    if (await OpenRecipeAsync(summary, current.Value.Id, current.Value.Current, token))
                    {
                        return true;
                    }
                }
            }

            // オフライン（または manor 未設定）。レシピ本体も画像も既に手元にあるので、
            // 工程番号さえあれば手元の控えから続きが出せる。
            var last = _lastSessionStore.Load();
            if (last == null || last.IsComplete || !_recipeStore.HasLocalRecipe(last.RecipeId))
            {
                return false;
            }

            var recipe = TryLoadLocalRecipe(last.RecipeId);
            if (recipe == null)
            {
                return false;
            }

            BeginCooking(recipe, last.SessionId, last.Current);
            Debug.Log($"[KitchenXR] 手元の控えから調理を再開しました: {last.RecipeId} 工程 {last.Current}");
            return true;
        }

        /// <summary>
        /// 一覧を出し直す。取れたら <c>index.json</c> へ写し、取れなければその写しを出す。
        /// 先頭は必ず見本。
        /// </summary>
        private async UniTask RefreshListAsync(CancellationToken token)
        {
            var items = new List<RecipeSummary>();
            if (_bundledSummary != null)
            {
                items.Add(_bundledSummary);
            }

            var status = string.Empty;
            if (!_manor.IsConfigured)
            {
                // 繋ぎ先が分からない・鍵が無い（ConnectManorAsync が理由を入れてある）。
                status = string.IsNullOrEmpty(_manorStatus) ? "manor が見つかりません（見本だけ）" : _manorStatus;
            }
            else
            {
                var listed = await _manor.ListRecipesAsync(token);
                if (listed.IsSuccess)
                {
                    _recipeStore.SaveIndexJson(listed.Value);
                }
                else
                {
                    status = listed.IsOffline ? "manor に繋がりません（控えた一覧）" : listed.Message;
                }

                // 取れても取れなくても読むのは写し——経路を1本にしておくと、
                // 「取れたときだけ出る欄」のようなものが混ざらない。
                var cached = _recipeStore.LoadIndexJson();
                if (!string.IsNullOrEmpty(cached))
                {
                    items.AddRange(RecipeListJson.Parse(cached));
                }
            }

            ShowListMode();
            _recipeListPanel?.Show(items, status);
        }

        // ---------------------------------------------------------------- 選ぶ・開く

        private void HandleRecipeSelected(RecipeSummary summary)
        {
            if (summary == null || _cts == null)
            {
                return;
            }

            SelectRecipeAsync(summary, _cts.Token).Forget();
        }

        private async UniTaskVoid SelectRecipeAsync(RecipeSummary summary, CancellationToken token)
        {
            // 見本は manor に無いので、セッションは作らない（進行はローカルの控えだけ）。
            int? sessionId = null;
            if (!summary.IsBundledSample && _manor.IsConfigured)
            {
                _recipeListPanel?.ShowBusy("準備中");
                sessionId = await StartOrResumeSessionAsync(summary.Id, token);
            }

            await OpenRecipeAsync(summary, sessionId, 0, token);
        }

        /// <summary>
        /// 調理を始める。manor は未終了のセッションを1件だけ持ち、`recipes.start_session` は
        /// 未終了があればレシピを問わずそれを返す——別のレシピの途中が残っていると進行が
        /// そちらに記録されてしまうので、先に <c>current</c> を見て終わらせてから始める。
        /// 手放した調理が <c>times_cooked</c> に数えられるのは承知の上（manor に「やめる」の口は無い）。
        /// </summary>
        private async UniTask<int?> StartOrResumeSessionAsync(string recipeId, CancellationToken token)
        {
            var current = await _manor.CurrentSessionAsync(token);
            if (current.IsSuccess && current.Value.Exists && current.Value.RecipeId != recipeId)
            {
                Debug.Log($"[KitchenXR] 別のレシピ（{current.Value.RecipeId}）の調理が残っていたので終わらせます。");
                await _manor.EndSessionAsync(current.Value.Id.Value, token);
            }

            var started = await _manor.StartSessionAsync(recipeId, token);
            return started.IsSuccess ? started.Value.Id : null;
        }

        /// <summary>
        /// レシピを開く。先に全部手元へ: 契約 JSON → <see cref="RecipeStore"/> へ写す →
        /// hero と全工程の画像 → それから調理を始める。
        /// その間、一覧の板は「準備中 n/m」の覆いを出して別の行を受け付けない。
        /// </summary>
        /// <param name="startStep">1 以上なら復帰（その工程から）。0 なら最初から。</param>
        /// <returns>開けたか。開けなければ一覧のまま。</returns>
        private async UniTask<bool> OpenRecipeAsync(
            RecipeSummary summary, int? sessionId, int startStep, CancellationToken token)
        {
            if (summary == null)
            {
                return false;
            }

            _recipeListPanel?.ShowBusy("準備中");

            var recipe = await FetchRecipeAsync(summary, token);
            if (recipe == null)
            {
                _recipeListPanel?.HideBusy();
                _recipeListPanel?.SetStatus("レシピを取れませんでした");
                return false;
            }

            // 画像を全部揃えてから始める（電子レンジで通信が切れても工程の写真が出るように）。
            await _recipeStore.PrepareAsync(
                recipe, (done, total) => _recipeListPanel?.ShowPreparing(done, total), token);

            if (token.IsCancellationRequested)
            {
                return false;
            }

            BeginCooking(recipe, sessionId, startStep);
            return true;
        }

        /// <summary>
        /// レシピ本体を手に入れる。順は「manor から取って写す → 手元のものを読む」。
        /// 見本（Resources）は manor に問い合わせず、初回だけ写して以後はローカルを読む。
        /// </summary>
        private async UniTask<Recipe> FetchRecipeAsync(RecipeSummary summary, CancellationToken token)
        {
            if (summary.IsBundledSample)
            {
                return TryLoadLocalRecipe(summary.Id, _bundledJson);
            }

            if (_manor.IsConfigured)
            {
                var fetched = await _manor.GetRecipeAsync(summary.Id, token);
                if (fetched.IsSuccess && !string.IsNullOrWhiteSpace(fetched.Value))
                {
                    _recipeStore.SaveRecipeJson(summary.Id, fetched.Value);
                }
            }

            return TryLoadLocalRecipe(summary.Id);
        }

        private Recipe TryLoadLocalRecipe(string recipeId, string bundledJson = null)
        {
            try
            {
                return _recipeStore.LoadRecipe(recipeId, bundledJson);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[KitchenXR] レシピを開けませんでした: {recipeId}（{e.Message}）");
                return null;
            }
        }

        /// <summary>調理の3枚に切り替えて、状態機械を立てる。</summary>
        private void BeginCooking(Recipe recipe, int? sessionId, int startStep)
        {
            _session = new CookSession(recipe);
            if (startStep >= 1)
            {
                _session.SeekTo(startStep);
            }

            _manorSessionId = sessionId;

            _recipePanel?.Bind(_recipeStore, recipe.Id);
            _ingredientsPanel?.BindRecipe(recipe);

            ShowCookingMode();
            RefreshAllPanels();
            SaveLastSession();
        }

        // ---------------------------------------------------------------- 進行

        private void HandleNext()
        {
            if (_session == null)
            {
                return;
            }

            _session.Apply(SessionEvent.NextRequested.Instance);
            RefreshAllPanels();
            RecordProgress("next");
        }

        private void HandlePrev()
        {
            if (_session == null)
            {
                return;
            }

            _session.Apply(SessionEvent.PrevRequested.Instance);
            RefreshAllPanels();
            RecordProgress("prev");
        }

        /// <summary>
        /// 進行を控えて、送れるなら送る。送れなくても調理は止まらない——
        /// 積むのはファイルへの追記1回で、送る試みは裏で回る。
        /// </summary>
        private void RecordProgress(string type)
        {
            SaveLastSession();

            _eventQueue.Enqueue(_manorSessionId, type, _session?.Current);
            if (_manor.IsConfigured && _cts != null)
            {
                _eventQueue.FlushAsync(_manor, _cts.Token).Forget();
            }
        }

        private void SaveLastSession()
        {
            if (_session == null)
            {
                return;
            }

            _lastSessionStore.Save(
                _session.Recipe.Id, _manorSessionId, _session.Current, _session.IsComplete);
        }

        /// <summary>
        /// 「作り終えた」（完了したときだけ出るボタン）。manor の <c>/end</c> へ送って一覧へ戻る。
        /// 送れなければ待ち行列に残り、次に繋がったときに送られる。
        /// </summary>
        private void HandleFinish()
        {
            if (_session == null)
            {
                return;
            }

            _eventQueue.Enqueue(_manorSessionId, CookEventQueue.EndType);
            _lastSessionStore.Clear();
            _manorSessionId = null;
            _session = null;

            if (_cts != null)
            {
                FinishAsync(_cts.Token).Forget();
            }
        }

        private async UniTaskVoid FinishAsync(CancellationToken token)
        {
            if (_manor.IsConfigured)
            {
                await _eventQueue.FlushAsync(_manor, token);
            }

            await RefreshListAsync(token);
        }

        /// <summary>
        /// 「一覧へ」（2度押し）。調理をやめるのではなく板を戻すだけ——manor 側のセッションは
        /// 未終了のまま残るので、次の起動ではそこから復帰する。手元の控えもそのまま残す。
        /// </summary>
        private void HandleBackToList()
        {
            if (_cts == null)
            {
                return;
            }

            BackToListAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid BackToListAsync(CancellationToken token)
        {
            await RefreshListAsync(token);
        }

        // ---------------------------------------------------------------- 表示の設定

        /// <summary>
        /// 文字と板の大きさを当てる。当てる先は一覧・レシピ・材料・タイマーの4枚。
        /// 動画の板は自分で寸法を決める（16:9 ⇄ 9:16）ので混ぜない。
        /// 配置の操作板と手のひらメニューも、出ている間だけの板なので対象外。
        /// </summary>
        private void ApplyDisplaySettings()
        {
            if (_displaySettings == null)
            {
                return;
            }

            DisplaySettingsApplier.Apply(
                _displaySettings, _recipeListPanel, _recipePanel, _ingredientsPanel, _timerPanel);

            _recipeListPanel?.SetDisplaySettings(_displaySettings.FontScale, _displaySettings.PanelScale);
        }

        /// <summary>押した瞬間に反映して控える（設定の板に「決定」を置かない）。</summary>
        private void HandleFontScaleSelected(DisplayScale scale)
        {
            if (_displaySettings == null || _displaySettings.FontScale == scale)
            {
                return;
            }

            _displaySettings.FontScale = scale;
            _displaySettings.Save();
            ApplyDisplaySettings();
        }

        private void HandlePanelScaleSelected(DisplayScale scale)
        {
            if (_displaySettings == null || _displaySettings.PanelScale == scale)
            {
                return;
            }

            _displaySettings.PanelScale = scale;
            _displaySettings.Save();
            ApplyDisplaySettings();
        }

        // ---------------------------------------------------------------- 配置モード

        /// <summary>
        /// 板と鍵を結ぶ（保存の単位はパネル1枚＝鍵1つ）。
        /// 一覧はレシピと同じ鍵——同じ場所に重ねて出す板なので、別々に覚える意味が無い。
        /// レシピの板を取っ手役（leader）にして、一覧はそれに付いていく。
        /// </summary>
        private void SetUpPlacement()
        {
            if (_panelPlacement == null)
            {
                return;
            }

            _panelPlacement.Bind(_anchorStore, _panelPoseFile, _handInputPolicy, _cookingModeInputGate);

            _panelPlacement.Register(PanelPlacement.RecipeKey, _recipePanel, _recipeListPanel);
            _panelPlacement.Register(PanelPlacement.IngredientsKey, _ingredientsPanel);
            _panelPlacement.Register(PanelPlacement.TimerKey, _timerPanel);
            _panelPlacement.Register(PanelPlacement.VideoKey, _videoPanel);

            // 調理中もレイで操作してよいのは動画の板だけ。
            // Register で全ての板が gate に登録された後に決める。
            _cookingModeInputGate?.AllowRayInCookingMode(_videoPanel);

            _panelPlacement.PlacementFinished += HandlePlacementFinished;
        }

        /// <summary>覚えている場所へ戻す（アンカー → 控え → 既定）。</summary>
        private async UniTaskVoid RestorePlacementAsync(CancellationToken token)
        {
            if (_panelPlacement == null)
            {
                return;
            }

            await _panelPlacement.RestoreAsync(token);
        }

        /// <summary>
        /// 配置モードへ入る（手のひらメニュー、またはレシピ／一覧の板の「配置」2度押し）。
        /// 操作の板は空間に出さない——頭の前に出すとレシピ／一覧の板と重なって当たり判定を
        /// 奪い合い、レイでも指でも押せなくなる。代わりに手のひらメニューを差し替える。
        /// </summary>
        private void HandlePlacementRequested()
        {
            if (_panelPlacement == null || _panelPlacement.IsPlacing)
            {
                return;
            }

            // 出口（保存・やめる）は手のひらメニューにしか無い。板が無いまま入ると
            // 配置モードから二度と出られなくなるので、入らない。
            if (_placementMenuPanel == null)
            {
                Debug.LogWarning(
                    "[KitchenXR] 手のひらメニューが無いので配置モードへ入りません（出口がありません）。");
                return;
            }

            // 「配置」はレシピ／一覧の板の頭からも押せる。そのときメニューが閉じていると
            // 「保存」「やめる」が押せず配置モードから出られないので、必ず開ける。
            // 手が1つも追えていなければ（コントローラだけのとき）メニューは出せない＝入らない。
            if (_wristMenu != null && !_wristMenu.Open())
            {
                Debug.LogWarning(
                    "[KitchenXR] 手が追えていないので配置モードへ入りません"
                    + "（メニューは手首に付くので、出口が作れません）。手をかざしてからもう一度どうぞ。");
                return;
            }

            _placementMenuPanel.SetPlacing(true);
            _panelPlacement.Enter();
        }

        private void HandlePlacementUndo()
        {
            _panelPlacement?.Undo();
            _placementMenuPanel?.SetHint("入る前の位置に戻しました");
        }

        private void HandlePlacementCancel() => _panelPlacement?.Cancel();

        private void HandlePlacementSave()
        {
            if (_panelPlacement == null || _cts == null)
            {
                return;
            }

            _placementMenuPanel?.SetHint("覚えています…");
            SavePlacementAsync(_cts.Token).Forget();
        }

        private async UniTaskVoid SavePlacementAsync(CancellationToken token)
        {
            await _panelPlacement.SaveAsync(token);
        }

        /// <summary>
        /// 「板を手元に」——迷子の板の救済。全部の板を今の頭の向きから決めた初期配置へ戻すので、
        /// 壁の奥・床の下・背中側へ行った板も必ず目の前に戻る。配置モードは続いたままなので、
        /// そのまま掴み直して「保存」で確定できる。「元に戻す」で取り消せるので1度押しでよい。
        /// </summary>
        private void HandlePlacementRecall()
        {
            if (_panelPlacement == null || !_panelPlacement.IsPlacing)
            {
                return;
            }

            PlaceInitialPanels();
            _placementMenuPanel?.SetHint("板を手元に並べ直しました");
        }

        /// <summary>
        /// 配置モードを出た（保存でも取り消しでも）。メニューを「配置」1つへ戻し、閉じる——
        /// 手を洗っているときなどに出ていると邪魔なので、用が済んだら手元に板を残さない。
        /// </summary>
        private void HandlePlacementFinished()
        {
            _placementMenuPanel?.SetPlacing(false);
            _wristMenu?.Close();
        }

        // ---------------------------------------------------------------- 板の出し入れ

        /// <summary>
        /// 一覧の板だけを出す（調理の3枚は引っ込める。動画の板はそのまま＝ながら見）。
        /// <see cref="GameObject.SetActive"/> ではなく <see cref="PanelVisibility"/> を通すのは、
        /// UIDocument が無効化のたびに rootVisualElement を作り直して、
        /// 各パネルが Awake で掴んだ要素の参照を殺してしまうため。
        /// </summary>
        private void ShowListMode()
        {
            PanelVisibility.SetVisible(_recipeListPanel, true);
            PanelVisibility.SetVisible(_recipePanel, false);
            PanelVisibility.SetVisible(_ingredientsPanel, false);
            PanelVisibility.SetVisible(_timerPanel, false);
        }

        /// <summary>調理の3枚（レシピ・材料・タイマー）を出す。</summary>
        private void ShowCookingMode()
        {
            PanelVisibility.SetVisible(_recipeListPanel, false);
            PanelVisibility.SetVisible(_recipePanel, true);
            PanelVisibility.SetVisible(_ingredientsPanel, true);
            PanelVisibility.SetVisible(_timerPanel, true);
        }

        private void RefreshAllPanels()
        {
            if (_session == null)
            {
                return;
            }

            _recipePanel?.Refresh(_session);
            _ingredientsPanel?.Refresh(_session);
            _timerPanel?.Refresh(_session);
        }

        // ---------------------------------------------------------------- 見本と動画

        /// <summary>
        /// 見本（Resources）を読んでおく。一覧の先頭に必ず並べるので、manor が寝ていても
        /// 1本は最後まで進められる。中身は開いたときに <see cref="RecipeStore"/> へ写す
        /// （表示の経路は1本のまま）。
        /// </summary>
        private void LoadBundledSample()
        {
            var textAsset = Resources.Load<TextAsset>(_recipeResourcePath);
            if (textAsset == null)
            {
                Debug.LogWarning($"[KitchenXR] 見本レシピが見つかりません: Resources/{_recipeResourcePath}.json");
                return;
            }

            _bundledJson = textAsset.text;

            try
            {
                var recipe = RecipeJson.Parse(_bundledJson);
                _bundledSummary = new RecipeSummary(
                    recipe.Id, $"見本: {recipe.Title}", recipe.TotalMinutes, "見本", null,
                    recipe.HeroImage, true);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[KitchenXR] 見本レシピの形が読めませんでした: {e.Message}");
                _bundledJson = null;
            }
        }

        /// <summary>id から一覧の行を引く（復帰のとき、題名を札に出すため）。</summary>
        private RecipeSummary FindSummary(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId))
            {
                return null;
            }

            if (_bundledSummary != null && _bundledSummary.Id == recipeId)
            {
                return _bundledSummary;
            }

            var cached = _recipeStore.LoadIndexJson();
            if (string.IsNullOrEmpty(cached))
            {
                return null;
            }

            foreach (var item in RecipeListJson.Parse(cached))
            {
                if (item.Id == recipeId)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 動画の一覧を読んで板へ配る。読めなくても板は立つ（「一覧がありません」を出す）——
        /// 娯楽の板なので、ここで落ちて調理が止まってはいけない。
        /// </summary>
        private async UniTaskVoid LoadMediaAsync(CancellationToken token)
        {
            if (_videoPanel == null)
            {
                return;
            }

            _mediaStore = MediaStore.CreateDefault();

            // まず手元（無ければ同梱の見本）。manor が寝ていても圏外でも、板はこれで立つ。
            var items = await _mediaStore.LoadAsync(token);
            if (_videoPanel != null)
            {
                _videoPanel.BindMedia(items);
            }

            // manor に繋がるなら動画リスト（Web で編集した一覧）を取って手元を上書きする。
            // 取れなければ黙って手元のまま（次の起動でまた試みる）。
            if (_manor == null || !_manor.IsConfigured)
            {
                return;
            }

            var listed = await _manor.ListMediaAsync(token);
            if (!listed.IsSuccess || token.IsCancellationRequested)
            {
                Debug.Log($"[KitchenXR] 動画リストは手元のまま（{listed.Message}）");
                return;
            }

            var remote = _mediaStore.SaveRemote(listed.Value);
            if (_videoPanel != null)
            {
                _videoPanel.BindMedia(remote);
            }
        }

        // ---------------------------------------------------------------- 初期配置

        /// <summary>
        /// 起動時に頭の前 0.8m・目線より少し下へ配る。
        /// 一覧の板はレシピの板と同じ場所（切り替えで入れ替わる）。
        /// </summary>
        private void PlaceInitialPanels()
        {
            var head = _headTransform != null ? _headTransform : Camera.main != null ? Camera.main.transform : null;
            if (head == null)
            {
                Debug.LogWarning("[KitchenXR] 頭のTransformが見つからないため、初期配置をスキップしました。");
                return;
            }

            var flatForward = head.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 1e-6f)
            {
                flatForward = Vector3.forward;
            }

            flatForward.Normalize();

            // パネルの「表」は local -Z（XRI World Space UI サンプルの向きに合わせた）。
            var rotation = Quaternion.LookRotation(flatForward, Vector3.up);
            var right = rotation * Vector3.right;

            var basePosition = head.position + flatForward * _forwardDistanceMeters + Vector3.down * _belowEyelineMeters;

            PlacePanel(_recipePanel != null ? _recipePanel.transform : null, basePosition, rotation);
            PlacePanel(_recipeListPanel != null ? _recipeListPanel.transform : null, basePosition, rotation);
            PlacePanel(_ingredientsPanel != null ? _ingredientsPanel.transform : null,
                basePosition - right * _lateralSpacingMeters, rotation);
            PlacePanel(_timerPanel != null ? _timerPanel.transform : null,
                basePosition + right * _lateralSpacingMeters, rotation);

            PlaceVideoPanel(basePosition, right, rotation);
        }

        /// <summary>
        /// 4枚目（動画）はレシピの右上＝タイマーの上。板の原点は左上なので、タイマーの板は
        /// 基準点から右下へ伸びている。その上辺から 4cm 空けて動画の板の下辺の中央を置く。
        /// 下辺を留めるのは、9:16 に切り替えると板が高くなり、上辺を留めると下へ食い込むから。
        /// </summary>
        private void PlaceVideoPanel(Vector3 basePosition, Vector3 right, Quaternion rotation)
        {
            if (_videoPanel == null)
            {
                return;
            }

            // タイマーの板の実幅は板そのものから読む。表示の設定（板の大きさ 小／大）で
            // localScale が変わるので、定数を写すと合わなくなる。
            var timerWidthMeters = 0.44f;
            var timerDocument = _timerPanel != null ? _timerPanel.GetComponent<UIDocument>() : null;
            if (timerDocument != null)
            {
                timerWidthMeters = timerDocument.worldSpaceSize.x
                    / WorldSpacePanelFactory.PanelPixelsPerUnit * _timerPanel.transform.localScale.x;
            }

            const float gapMeters = 0.04f;

            var timerCenter = basePosition + right * (_lateralSpacingMeters + timerWidthMeters / 2f);
            _videoPanel.PlaceAtBottomCenter(timerCenter + Vector3.up * gapMeters, rotation);
        }

        private static void PlacePanel(Transform panel, Vector3 position, Quaternion rotation)
        {
            if (panel == null)
            {
                return;
            }

            panel.SetPositionAndRotation(position, rotation);
        }
    }
}
