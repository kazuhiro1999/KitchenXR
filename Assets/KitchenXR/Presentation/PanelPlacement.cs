using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Platform;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 板の置き場所を覚える／置き直す（ROADMAP P2・設計 §4.3・§4.4）。
    ///
    /// 主人の言葉では「空間を覚えていることは、毎日使う上で快適にするためには必須」（設計 §0 A）。
    /// つまりアンカーは飾りではなく土台なので、ここは**必ず何かに落ちる**ように組んである:
    ///
    ///   起動時の復元順  アンカー → 控え（panels.json） → 既定（Bootstrap が頭の前へ配った位置）
    ///   保存の順        アンカーへ保存（できなくても続ける）＋ 控えへ**必ず**書く
    ///
    /// 控えを「アンカーが使えないときだけ」にしないのが肝心。アンカーの復元は、部屋が変わった・
    /// 空間の地図が作り直された・鍵を失った等で普通に失敗する。そのときに落ちる先が要る。
    ///
    /// **鍵は板1枚＝1つ**（設計 §4.3。台所全体の座標系は作らない）。レシピと一覧は**同じ鍵**——
    /// 同じ場所に重ねて出す板なので（P3 で決めた）、別々に覚える意味が無い。
    ///
    /// 配置モード（設計 §4.4）:
    ///   入る   手のひらメニューの「配置」、またはレシピ／一覧の板の頭の「配置」（2度押し）
    ///   最中   全ての板に枠と取っ手が出て、Ray ＋ Grab で掴んで動かせる。
    ///          **調理の板の UI は効かない**（誤って「次へ」が進まない）
    ///   出る   「保存」→ 全部の板を <see cref="IAnchorStore"/> と控えへ → 調理モードへ戻る
    ///          「元に戻す」→ 入る前の位置へ／「やめる」→ 元に戻して調理モードへ
    /// </summary>
    public sealed class PanelPlacement : MonoBehaviour
    {
        public const string RecipeKey = "panel.recipe";
        public const string IngredientsKey = "panel.ingredients";
        public const string TimerKey = "panel.timer";
        public const string VideoKey = "panel.video";

        [Header("控えの基準（XR Origin。未指定なら世界座標をそのまま書く）")]
        [SerializeField] private Transform _originTransform;

        [Header("復元した板をゆっくり出す秒数（設計: 突然現れないように）")]
        [SerializeField] private float _revealSeconds = 0.6f;

        private readonly List<Entry> _entries = new List<Entry>();

        private IAnchorStore _anchors;
        private PanelPoseFile _poseFile;
        private IHandInputPolicy _policy;
        private CookingModeInputGate _gate;

        /// <summary>配置モードに入る前の姿（「元に戻す」「やめる」で戻す先）。</summary>
        private readonly Dictionary<string, Pose> _poseBeforePlacement = new Dictionary<string, Pose>();

        public bool IsPlacing { get; private set; }

        /// <summary>復元した板を出すのにかける秒数（0 なら即座に置く）。試験から短くできるように公開する。</summary>
        public float RevealSeconds
        {
            get => _revealSeconds;
            set => _revealSeconds = value;
        }

        /// <summary>配置モードを出た（保存・取り消しのどちらでも）。Bootstrap が板の出し入れを戻す契機。</summary>
        public event Action PlacementFinished;

        /// <summary>鍵ごとに、アンカー（true）／控え（false）／既定（未登場）のどれで戻したか。試験と診断用。</summary>
        public IReadOnlyDictionary<string, string> RestoreSources => _restoreSources;

        private readonly Dictionary<string, string> _restoreSources = new Dictionary<string, string>();

        public const string SourceAnchor = "anchor";
        public const string SourceFile = "file";
        public const string SourceDefault = "default";

        // ---------------------------------------------------------------- 組み立て

        public void Bind(
            IAnchorStore anchors, PanelPoseFile poseFile, IHandInputPolicy policy,
            CookingModeInputGate gate = null, Transform origin = null)
        {
            _anchors = anchors;
            _poseFile = poseFile;
            _policy = policy;
            _gate = gate;

            if (origin != null)
            {
                _originTransform = origin;
            }

            _poseFile?.Load();
        }

        /// <summary>
        /// 鍵1つに板を結ぶ。<paramref name="leader"/> が掴んで動かす板で、
        /// <paramref name="followers"/> は同じ場所へ連れて行かれる板（一覧はレシピに付いていく）。
        /// </summary>
        public void Register(string key, Component leader, params Component[] followers)
        {
            if (string.IsNullOrEmpty(key) || leader == null)
            {
                return;
            }

            var entry = new Entry(key, leader.transform);
            if (followers != null)
            {
                foreach (var follower in followers)
                {
                    if (follower != null)
                    {
                        entry.Followers.Add(follower.transform);
                    }
                }
            }

            _entries.Add(entry);

            // 調理モードで UI を効かせる／配置モードで効かなくするのは入力の切り替え役の仕事。
            _gate?.AddUiPanel(entry.Leader.gameObject);
            foreach (var follower in entry.Followers)
            {
                _gate?.AddUiPanel(follower.gameObject);
            }
        }

        // ---------------------------------------------------------------- 起動時の復元

        /// <summary>
        /// 覚えている場所へ板を戻す。順は**アンカー → 控え → 既定**。
        /// 既定（＝呼ばれた時点の Transform）のときは何も動かさない。
        /// 戻した板は <see cref="_revealSeconds"/> かけてゆっくり出す。
        /// </summary>
        public async UniTask RestoreAsync(CancellationToken token = default)
        {
            _restoreSources.Clear();

            foreach (var entry in _entries)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var restored = await ResolvePoseAsync(entry.Key);
                if (restored == null)
                {
                    _restoreSources[entry.Key] = SourceDefault;
                    continue;
                }

                RevealTo(entry, restored.Value);
            }
        }

        /// <summary>アンカー → 控え の順に引く。どちらも無ければ null（＝既定のまま）。</summary>
        private async UniTask<Pose?> ResolvePoseAsync(string key)
        {
            if (_anchors != null)
            {
                Pose? fromAnchor = null;
                try
                {
                    fromAnchor = await _anchors.LoadAsync(key);
                }
                catch (Exception e)
                {
                    // 口の実装が投げても起動は止めない（控えへ落ちる）。
                    Debug.LogWarning($"[KitchenXR] アンカーの読み出しで例外（{key}）: {e.Message}");
                }

                if (fromAnchor.HasValue)
                {
                    _restoreSources[key] = SourceAnchor;
                    return fromAnchor;
                }
            }

            if (_poseFile != null && _poseFile.TryGet(key, out var relative))
            {
                _restoreSources[key] = SourceFile;
                return ToWorld(relative);
            }

            return null;
        }

        // ---------------------------------------------------------------- 配置モード

        /// <summary>配置モードへ入る（設計 §4.4）。</summary>
        public void Enter()
        {
            if (IsPlacing)
            {
                return;
            }

            IsPlacing = true;

            _poseBeforePlacement.Clear();
            foreach (var entry in _entries)
            {
                _poseBeforePlacement[entry.Key] = WorldPoseOf(entry.Leader);

                // 動かす対象は全部見えていないと置けない。一覧のように「今は引っ込んでいる板」は
                // 取っ手役（leader）だけを出し、付いていく板は隠したままにする。
                entry.RememberVisibility();
                PanelVisibility.SetVisible(entry.Leader.gameObject, true);

                SetGrabbable(entry, true);
                SetFrameVisible(entry.Leader.gameObject, true);
            }

            _policy?.SetMode(HandInputMode.PlacementMode);
        }

        /// <summary>「元に戻す」——入る前の位置へ戻す（配置モードからは出ない）。</summary>
        public void Undo()
        {
            if (!IsPlacing)
            {
                return;
            }

            foreach (var entry in _entries)
            {
                if (_poseBeforePlacement.TryGetValue(entry.Key, out var pose))
                {
                    ApplyPose(entry, pose);
                }
            }
        }

        /// <summary>「やめる」——元に戻して調理モードへ。</summary>
        public void Cancel()
        {
            if (!IsPlacing)
            {
                return;
            }

            Undo();
            Leave();
        }

        /// <summary>
        /// 「保存」——全部の板を覚えて調理モードへ戻る。
        ///
        /// **アンカーと控えの両方へ書く。** アンカーが使えない機（Editor・非対応機）でも
        /// 控えだけは残るし、アンカーが使える機でも復元に失敗したときの受け皿になる。
        /// </summary>
        public async UniTask SaveAsync(CancellationToken token = default)
        {
            var anchored = 0;

            foreach (var entry in _entries)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                var world = WorldPoseOf(entry.Leader);

                if (_anchors != null)
                {
                    try
                    {
                        if (await _anchors.SaveAsync(entry.Key, world))
                        {
                            anchored++;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[KitchenXR] アンカーの保存で例外（{entry.Key}）: {e.Message}");
                    }
                }

                _poseFile?.Set(entry.Key, ToRelative(world));
            }

            _poseFile?.Save();

            Debug.Log($"[KitchenXR] 板の位置を覚えました（アンカー {anchored}/{_entries.Count} 枚・控えは全部）。");

            Leave();
        }

        /// <summary>配置モードを出て調理モードへ（板の出し入れは Bootstrap が戻す）。</summary>
        private void Leave()
        {
            if (!IsPlacing)
            {
                return;
            }

            IsPlacing = false;

            foreach (var entry in _entries)
            {
                SetFrameVisible(entry.Leader.gameObject, false);
                SetGrabbable(entry, false);

                // 付いていく板を取っ手役に揃える（一覧はレシピと同じ場所・同じ向き）。
                ApplyPose(entry, WorldPoseOf(entry.Leader));

                entry.RestoreVisibility();
            }

            _policy?.SetMode(HandInputMode.CookingMode);
            PlacementFinished?.Invoke();
        }

        // ---------------------------------------------------------------- 掴めるようにする

        /// <summary>
        /// 配置モードの間だけ「掴める板」にする（設計 §4.4 の Ray ＋ Grab）。
        ///
        /// <see cref="XRSimpleInteractable"/>（ポークの受け口）と
        /// <see cref="XRGrabInteractable"/> は**同時に有効にしない**——
        /// XRI は「コライダー1つに Interactable 1つ」で引き当てるので、
        /// 同じ BoxCollider を2つが名乗ると片方が上書きされて、どちらが勝つか読めなくなる。
        /// </summary>
        private static void SetGrabbable(Entry entry, bool grabbable)
        {
            var go = entry.Leader.gameObject;
            var simple = go.GetComponent<XRSimpleInteractable>();

            if (grabbable)
            {
                // 先にポークの受け口を降ろしてから掴む仕掛けを立てる。逆にすると、
                // 足した瞬間の一拍だけ同じコライダーを2つが名乗って XRI が警告を出す。
                entry.SimpleInteractableWasEnabled = simple != null && simple.enabled;
                if (simple != null)
                {
                    simple.enabled = false;
                }

                var added = EnsureGrabInteractable(go);
                if (added != null)
                {
                    added.enabled = true;
                }

                return;
            }

            var grab = go.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.enabled = false;
            }

            if (simple != null)
            {
                simple.enabled = entry.SimpleInteractableWasEnabled;
            }
        }

        /// <summary>
        /// 掴む仕掛けを（無ければ）足す。<see cref="XRGrabInteractable"/> は Rigidbody を要求するので、
        /// 動かない（kinematic・重力なし）Rigidbody を一緒に置く——板が落ちたり弾かれたりしないように。
        /// 掴み口は「触れたところ」（<c>useDynamicAttach</c>）にして、遠くから引き寄せても
        /// 板が手元へ飛んでこないようにする。
        /// </summary>
        private static XRGrabInteractable EnsureGrabInteractable(GameObject go)
        {
            var grab = go.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                return grab;
            }

            var body = go.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = go.AddComponent<Rigidbody>();
            }

            body.isKinematic = true;
            body.useGravity = false;

            grab = go.AddComponent<XRGrabInteractable>();
            grab.enabled = false;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.trackRotation = true;
            grab.throwOnDetach = false;

            var collider = go.GetComponent<BoxCollider>();
            if (collider != null)
            {
                grab.colliders.Clear();
                grab.colliders.Add(collider);
            }

            return grab;
        }

        // ---------------------------------------------------------------- 枠と取っ手

        /// <summary>
        /// 配置モードの目印（設計 §4.4「パネルに枠と取っ手が出る」）。
        /// USS ではなく手で書いた寸法にしてあるのは、板ごとに別の uxml／uss を持っていて、
        /// 5枚全部に同じ規則を足すより1か所で描いたほうが後から動かしやすいため。
        /// <c>pickingMode</c> は Ignore——枠が指やレイを吸ってはいけない。
        /// </summary>
        public static void SetFrameVisible(GameObject panel, bool visible)
        {
            if (panel == null)
            {
                return;
            }

            var document = panel.GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            if (root == null)
            {
                return;
            }

            var frame = root.Q<VisualElement>(FrameName);
            if (!visible)
            {
                frame?.RemoveFromHierarchy();
                return;
            }

            if (frame != null)
            {
                return;
            }

            frame = new VisualElement { name = FrameName, pickingMode = PickingMode.Ignore };
            frame.style.position = Position.Absolute;
            frame.style.left = 0f;
            frame.style.right = 0f;
            frame.style.top = 0f;
            frame.style.bottom = 0f;
            frame.style.borderTopWidth = 1.5f;
            frame.style.borderBottomWidth = 1.5f;
            frame.style.borderLeftWidth = 1.5f;
            frame.style.borderRightWidth = 1.5f;

            var accent = new Color(0.961f, 0.620f, 0.043f); // theme.uss の --color-accent (#F59E0B)。
            frame.style.borderTopColor = accent;
            frame.style.borderBottomColor = accent;
            frame.style.borderLeftColor = accent;
            frame.style.borderRightColor = accent;
            frame.style.borderTopLeftRadius = 12f;
            frame.style.borderTopRightRadius = 12f;
            frame.style.borderBottomLeftRadius = 12f;
            frame.style.borderBottomRightRadius = 12f;

            // 取っ手（板の頭の帯）。掴むのは板のどこでもよいが、「掴める」と分かる印を出す。
            var handle = new VisualElement { name = HandleName, pickingMode = PickingMode.Ignore };
            handle.style.position = Position.Absolute;
            handle.style.top = 2f;
            handle.style.left = Length.Percent(38f);
            handle.style.width = Length.Percent(24f);
            handle.style.height = 3f;
            handle.style.backgroundColor = accent;
            handle.style.borderTopLeftRadius = 1.5f;
            handle.style.borderTopRightRadius = 1.5f;
            handle.style.borderBottomLeftRadius = 1.5f;
            handle.style.borderBottomRightRadius = 1.5f;
            frame.Add(handle);

            root.Add(frame);
        }

        public const string FrameName = "placementFrame";
        public const string HandleName = "placementHandle";

        /// <summary>枠が出ているか（試験用）。</summary>
        public static bool HasFrame(GameObject panel)
        {
            var document = panel != null ? panel.GetComponent<UIDocument>() : null;
            var root = document != null ? document.rootVisualElement : null;
            return root != null && root.Q<VisualElement>(FrameName) != null;
        }

        // ---------------------------------------------------------------- 座標の出し入れ

        private static Pose WorldPoseOf(Transform t) => new Pose(t.position, t.rotation);

        private Pose ToRelative(Pose world)
        {
            if (_originTransform == null)
            {
                return world;
            }

            return new Pose(
                _originTransform.InverseTransformPoint(world.position),
                Quaternion.Inverse(_originTransform.rotation) * world.rotation);
        }

        private Pose ToWorld(Pose relative)
        {
            if (_originTransform == null)
            {
                return relative;
            }

            return new Pose(
                _originTransform.TransformPoint(relative.position),
                _originTransform.rotation * relative.rotation);
        }

        private static void ApplyPose(Entry entry, Pose pose)
        {
            entry.Leader.SetPositionAndRotation(pose.position, pose.rotation);
            foreach (var follower in entry.Followers)
            {
                follower.SetPositionAndRotation(pose.position, pose.rotation);
            }
        }

        /// <summary>
        /// 覚えていた場所へ**ゆっくり**戻す。いきなり現れると「勝手に動いた」に見えるので、
        /// 今の場所から目的地へ短く補間しながら、板を透明から不透明へ持ち上げる。
        /// </summary>
        private void RevealTo(Entry entry, Pose target)
        {
            if (_revealSeconds <= 0f || !isActiveAndEnabled)
            {
                ApplyPose(entry, target);
                return;
            }

            StartCoroutine(RevealRoutine(entry, target));
        }

        private IEnumerator RevealRoutine(Entry entry, Pose target)
        {
            var from = WorldPoseOf(entry.Leader);
            var document = entry.Leader.GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;

            var elapsed = 0f;
            while (elapsed < _revealSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / _revealSeconds);
                var eased = t * t * (3f - 2f * t); // smoothstep。端で速度が 0 になる。

                ApplyPose(entry, new Pose(
                    Vector3.Lerp(from.position, target.position, eased),
                    Quaternion.Slerp(from.rotation, target.rotation, eased)));

                if (root != null)
                {
                    root.style.opacity = eased;
                }

                yield return null;
            }

            ApplyPose(entry, target);
            if (root != null)
            {
                root.style.opacity = 1f;
            }
        }

        // ---------------------------------------------------------------- 中身

        private sealed class Entry
        {
            public Entry(string key, Transform leader)
            {
                Key = key;
                Leader = leader;
            }

            public string Key { get; }
            public Transform Leader { get; }
            public List<Transform> Followers { get; } = new List<Transform>();

            public bool SimpleInteractableWasEnabled { get; set; } = true;

            private readonly List<bool> _visibilityBefore = new List<bool>();

            /// <summary>配置モードに入る前の「出ている／引っ込んでいる」を覚える。</summary>
            public void RememberVisibility()
            {
                _visibilityBefore.Clear();
                _visibilityBefore.Add(PanelVisibility.IsVisible(Leader));
                foreach (var follower in Followers)
                {
                    _visibilityBefore.Add(PanelVisibility.IsVisible(follower));
                }
            }

            public void RestoreVisibility()
            {
                if (_visibilityBefore.Count == 0)
                {
                    return;
                }

                PanelVisibility.SetVisible(Leader.gameObject, _visibilityBefore[0]);
                for (var i = 0; i < Followers.Count && i + 1 < _visibilityBefore.Count; i++)
                {
                    PanelVisibility.SetVisible(Followers[i].gameObject, _visibilityBefore[i + 1]);
                }
            }
        }
    }
}
