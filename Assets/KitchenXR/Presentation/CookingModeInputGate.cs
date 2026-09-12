using System.Collections.Generic;
using KitchenXR.Platform;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 2つのモードで手の入力を切り替える（設計 §4.4・§7・§11 追補 2026-09-13）。
    ///
    /// | | 配置モード | 調理モード（既定） |
    /// |---|---|---|
    /// | Ray（NearFar・XRRay）そのもの | 生きる | **生きる**（v1.0.7 までは切っていた） |
    /// | Ray が届く板 | 全部 | **動画の板だけ** |
    /// | 調理の板の UI（ポークで押す釦） | **効かない** | 効く |
    ///
    /// XRI は機種非依存のツールキットそのものなので、この配線は Platform/ に閉じ込める対象では
    /// ない（設計 §4.2）。
    ///
    /// **ポークの Interactor そのものは切らない**（P2 でのずらし。理由を残す）:
    /// 配置モードで切ってしまうと手のひらメニューも指で押せなくなり、
    /// レイが実機で UI に届かなかったときに配置モードから出られなくなる。
    /// 代わりに**調理の板の UI を効かなくする**（板ガラスをかぶせる）——
    /// 狙い（設計 §4.4「配置モードでは Poke のボタンは効かない」＝誤って工程が進まない）は
    /// これで満たしつつ、掴む（コライダー）は生きたままにできる。
    ///
    /// ---------------------------------------------------------------------------
    /// 2026-09-13 主人の実機確認（v1.0.7）でここを作り直した。主人の言葉:
    ///   「Youtube プレイヤーだけレイ操作を有効化してほしい（基本、料理中は前面にレシピを
    ///     出すのでレイは邪魔だが、Youtube は少し離れた場所に置くので、逆に遠隔から操作したい）」
    ///   「壁の奥に行ってしまったらつかめないので何とかしたい」
    ///
    /// Ray を丸ごと on/off するのをやめ、**層で分ける**。層は2種類を重ねてある——
    /// 片方だけでは足りないので、両方要る:
    ///
    ///   1. **Interaction Layer**（XRI の層。<c>InteractionLayerSettings</c> の 1 番 = "Video"）
    ///      調理モードでは Ray の <c>interactionLayers</c> を Video だけにする。
    ///      これで XRI の掴み・ホバー・振動が動画の板以外に効かなくなる。
    ///
    ///   2. **物理の層**（8 番 "Kitchen Panel Off Ray"。この用途のために足した）
    ///      Interaction Layer は **UI Toolkit の当たり**には効かない——XRI のワールド空間 UI
    ///      （<c>XRUIToolkitHandler</c>）は「レイが当たったコライダーに UIDocument が付いているか」
    ///      だけを見ていて、Interactable も Interaction Layer も通らない。
    ///      つまり 1 だけだと、調理中にレイを向けて摘まむだけでレシピの「次へ」が押せてしまう。
    ///      そこで調理モードの間、**動画以外の板の GameObject を 8 番へ移す**。
    ///      MR テンプレートの Ray（Near-Far Interactor）の far 側の raycastMask は
    ///      Default(0)／UI(5)／XR Simulation(31) の3つだけなので、8 番はレイに引っ掛からない。
    ///      配置モードでは元の層（Default）へ戻すので、壁の奥の板もレイで掴める。
    ///
    ///      **2 番 "Ignore Raycast" ではだめ**（2026-09-13 に PlayMode 試験で踏んだ）。
    ///      ポークの UI は XRI が座標を投げ、その先で **UI Toolkit 自身**がワールド空間の板を
    ///      引き当てる。そこは <c>Physics.DefaultRaycastLayers</c>（＝2 番だけを外した全部）で
    ///      当たりを取るので、板を 2 番へ移すと**指でも押せなくなる**。
    ///      8 番なら DefaultRaycastLayers には入ったまま（＝ポークは効く）で、
    ///      Ray の raycastMask からは外れる（＝レイは届かない）。
    /// </summary>
    public sealed class CookingModeInputGate : MonoBehaviour
    {
        /// <summary>
        /// XRI の Interaction Layer 「Video」の番号（<c>Assets/XRI/Settings/Resources/
        /// InteractionLayerSettings.asset</c> の 1 番）。動画の板だけがこの層を名乗る。
        /// </summary>
        public const int VideoInteractionLayer = 1;

        /// <summary>
        /// 物理層「Kitchen Panel Off Ray」（<c>ProjectSettings/TagManager.asset</c> の 8 番）。
        /// 調理モードの間、動画以外の板を置く先。Ray の raycastMask には入っていないが
        /// <c>Physics.DefaultRaycastLayers</c> には入っている——上の説明を見よ。
        /// </summary>
        public const int OffRayPhysicsLayer = 8;

        /// <summary>この層の名前（シーンを組むときに TagManager と食い違っていないか確かめる）。</summary>
        public const string OffRayPhysicsLayerName = "Kitchen Panel Off Ray";

        /// <summary>調理モードで Ray が触れてよい層（動画の板だけ）。</summary>
        public static int CookingRayInteractionLayers => 1 << VideoInteractionLayer;

        /// <summary>配置モードで Ray が触れてよい層（全部。壁の奥の板も掴めるように）。</summary>
        public static int PlacementRayInteractionLayers => ~0;

        [SerializeField]
        [Tooltip("Ray っぽい Interactor（NearFarInteractor・XRRayInteractor 等）。Poke は含めない。"
                 + "常に有効のままで、触れてよい層だけをモードで変える。")]
        private Behaviour[] _rayLikeInteractors = System.Array.Empty<Behaviour>();

        [SerializeField]
        [Tooltip("配置モードの間だけ UI を効かなくする板（調理の板。手のひらメニューは入れない）。")]
        private List<GameObject> _uiPanels = new List<GameObject>();

        /// <summary>板ごとの元の物理層。調理モードで移した板を配置モードで戻すのに要る。</summary>
        private readonly Dictionary<GameObject, int> _panelHomeLayer = new Dictionary<GameObject, int>();

        /// <summary>調理中もレイが届いてよい板（動画の板。主人の指示）。</summary>
        private GameObject _rayReachableWhileCooking;

        private IHandInputPolicy _policy;

        public void Bind(IHandInputPolicy policy)
        {
            if (_policy != null)
            {
                _policy.ModeChanged -= OnModeChanged;
            }

            _policy = policy;
            _policy.ModeChanged += OnModeChanged;
            Apply(_policy.CurrentMode);
        }

        /// <summary>
        /// モードで層を切り替える Interactor を差し替える（シーンでの配線と等価。試験から使う）。
        /// </summary>
        public void SetRayLikeInteractors(params Behaviour[] interactors)
        {
            _rayLikeInteractors = interactors ?? System.Array.Empty<Behaviour>();

            if (_policy != null)
            {
                Apply(_policy.CurrentMode);
            }
        }

        /// <summary>
        /// 配置モードで UI を止める板を足す（<see cref="PanelPlacement"/> が登録のついでに呼ぶ。
        /// シーンで挿し忘れても板の一覧が1つにまとまるように）。
        /// </summary>
        public void AddUiPanel(GameObject panel)
        {
            if (panel == null || _uiPanels.Contains(panel))
            {
                return;
            }

            _uiPanels.Add(panel);
            _panelHomeLayer[panel] = panel.layer;

            if (_policy != null)
            {
                ApplyToPanel(panel, _policy.CurrentMode == HandInputMode.PlacementMode);
            }
        }

        /// <summary>
        /// 調理中もレイで操作してよい板を決める（動画の板。主人「Youtube だけ遠隔から操作したい」）。
        /// この板だけは調理モードでも元の物理層に残る。
        /// Interaction Layer の側（Video を名乗らせる）は板を組み立てるときに決めてある
        /// （<see cref="WorldSpacePanelFactory.Configure"/> の <c>interactionLayers</c>）。
        /// </summary>
        public void AllowRayInCookingMode(Component panel)
        {
            _rayReachableWhileCooking = panel != null ? panel.gameObject : null;

            if (_rayReachableWhileCooking != null && _policy != null)
            {
                ApplyToPanel(_rayReachableWhileCooking, _policy.CurrentMode == HandInputMode.PlacementMode);
            }
        }

        private void OnDestroy()
        {
            if (_policy != null)
            {
                _policy.ModeChanged -= OnModeChanged;
            }
        }

        private void OnModeChanged(HandInputMode mode) => Apply(mode);

        private void Apply(HandInputMode mode)
        {
            var placing = mode == HandInputMode.PlacementMode;

            foreach (var interactor in _rayLikeInteractors)
            {
                if (interactor == null)
                {
                    continue;
                }

                // v1.0.7 まではここで enabled を切っていた。切ると壁の奥へ行った板に手が届かない
                // （主人「壁の奥に行ってしまったらつかめない」）ので、常に生かして層で絞る。
                interactor.enabled = true;

                if (interactor is XRBaseInteractor rayLike)
                {
                    rayLike.interactionLayers =
                        placing ? PlacementRayInteractionLayers : CookingRayInteractionLayers;
                }
            }

            foreach (var panel in _uiPanels)
            {
                ApplyToPanel(panel, placing);
            }
        }

        private void ApplyToPanel(GameObject panel, bool placing)
        {
            if (panel == null)
            {
                return;
            }

            SetUiEnabled(panel, !placing);

            // 配置モードでは全部の板にレイを届かせる。調理モードでは動画の板だけ。
            var reachable = placing || panel == _rayReachableWhileCooking;
            var home = _panelHomeLayer.TryGetValue(panel, out var layer) ? layer : 0;
            panel.layer = reachable ? home : OffRayPhysicsLayer;
        }

        /// <summary>レイが今この板に届くか（試験用）。</summary>
        public static bool IsRayReachable(GameObject panel) =>
            panel != null && panel.layer != OffRayPhysicsLayer;

        /// <summary>板の UI を覆う板ガラスの名前。</summary>
        public const string BlockerName = "inputBlocker";

        /// <summary>
        /// 板の UI が入力を受けるか。掴む側（コライダー）には触らない。
        ///
        /// やり方は**板ガラス**（板全体を覆う、当たるだけで何もしない要素）を1枚かぶせること。
        /// <c>pickingMode = Ignore</c> を根に立てても子は拾われてしまうし、
        /// <c>SetEnabled(false)</c> は既定テーマの「無効」の見た目（薄い灰）を全部に付けてしまう。
        /// 板ガラスなら、当たり判定だけを確実に奪って見た目は変えない。
        /// </summary>
        private static void SetUiEnabled(GameObject panel, bool enabled)
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

            var blocker = root.Q<VisualElement>(BlockerName);

            if (enabled)
            {
                blocker?.RemoveFromHierarchy();
                return;
            }

            if (blocker != null)
            {
                blocker.BringToFront();
                return;
            }

            blocker = new VisualElement { name = BlockerName, pickingMode = PickingMode.Position };
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0f;
            blocker.style.right = 0f;
            blocker.style.top = 0f;
            blocker.style.bottom = 0f;
            root.Add(blocker);
        }

        /// <summary>板の UI が今効いているか（試験用）。</summary>
        public static bool IsUiEnabled(GameObject panel)
        {
            var document = panel != null ? panel.GetComponent<UIDocument>() : null;
            var root = document != null ? document.rootVisualElement : null;
            return root != null && root.Q<VisualElement>(BlockerName) == null;
        }
    }
}
