using System.Collections.Generic;
using KitchenXR.Platform;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 2つのモードで手の入力を切り替える。Ray が届く板は「配置＝全部／調理＝動画の板だけ」、
    /// 調理の板の UI は「配置＝効かない／調理＝効く」。
    ///
    /// Interactor は切らず、板の側で絞る。ポークを切ると手のひらメニューも押せず配置モードから
    /// 出られなくなるので、調理の板の UI は板ガラスで覆う（掴むコライダーは生きたまま）。
    ///
    /// Ray は層で絞り、層は2種類要る。XRI の Interaction Layer（1 番 "Video"）だけでは
    /// UI Toolkit の当たりに効かず（<c>XRUIToolkitHandler</c> はコライダーに UIDocument が
    /// 付いているかだけを見る）、レイで摘まむだけで「次へ」が押せてしまうので、物理層
    /// （8 番 "Kitchen Panel Off Ray"）へも移す。8 番は Ray の raycastMask（0/5/31）から外れ、
    /// <c>Physics.DefaultRaycastLayers</c> には入ったまま——2 番 "Ignore Raycast" だと後者からも
    /// 外れて指でも押せなくなる。
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

        /// <summary>調理中もレイが届いてよい板（動画の板）。</summary>
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
        /// 調理中もレイで操作してよい板を決める（動画の板）。
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

                // enabled を切ると壁の奥へ行った板に手が届かないので、常に生かして層で絞る。
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
        /// やり方は板ガラス（板全体を覆う、当たるだけで何もしない要素）を1枚かぶせること。
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
