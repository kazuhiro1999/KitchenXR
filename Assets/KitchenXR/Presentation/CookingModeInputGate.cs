using System.Collections.Generic;
using KitchenXR.Platform;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 2つのモードで手の入力を切り替える（設計 §4.4・§7）。
    ///
    /// | | 配置モード | 調理モード（既定） |
    /// |---|---|---|
    /// | Ray（NearFar・XRRay） | 効く | **切る** |
    /// | 調理の板の UI（ポークで押す釦） | **効かない** | 効く |
    ///
    /// XRI は機種非依存のツールキットそのものなので、この配線は Platform/ に閉じ込める対象では
    /// ない（設計 §4.2）。
    ///
    /// **ポークの Interactor そのものは切らない**（P2 でのずらし。理由を残す）:
    /// 配置モードで切ってしまうと、「保存」の板も手のひらメニューも指で押せなくなり、
    /// レイが実機で UI に届かなかったときに配置モードから出られなくなる。
    /// 代わりに**調理の板の UI を効かなくする**（<c>rootVisualElement.pickingMode = Ignore</c>）——
    /// 狙い（設計 §4.4「配置モードでは Poke のボタンは効かない」＝誤って工程が進まない）は
    /// これで満たしつつ、掴む（コライダー）は生きたままにできる。
    /// </summary>
    public sealed class CookingModeInputGate : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("調理モードで無効にする Interactor（NearFarInteractor・XRRayInteractor 等）。Poke は含めない。")]
        private Behaviour[] _rayLikeInteractors = System.Array.Empty<Behaviour>();

        [SerializeField]
        [Tooltip("配置モードの間だけ UI を効かなくする板（調理の板。配置の板と手のひらメニューは入れない）。")]
        private List<GameObject> _uiPanels = new List<GameObject>();

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
        /// 調理モードで止める Interactor を差し替える（シーンでの配線と等価。試験から使う）。
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

            if (_policy != null)
            {
                SetUiEnabled(panel, _policy.CurrentMode == HandInputMode.CookingMode);
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
                if (interactor != null)
                {
                    interactor.enabled = placing;
                }
            }

            foreach (var panel in _uiPanels)
            {
                SetUiEnabled(panel, !placing);
            }
        }

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
