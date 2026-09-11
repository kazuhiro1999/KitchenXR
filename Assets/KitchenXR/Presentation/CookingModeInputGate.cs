using KitchenXR.Platform;
using UnityEngine;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 調理モードでは Poke（触る）だけ。Ray とピンチは無効にする（設計 §4.4・§7）。
    /// XRI は機種非依存のツールキットそのものなので、この配線は Platform/ に閉じ込める対象ではない
    /// （設計 §4.2。ここで無効にするのは NearFarInteractor・XRRayInteractor など「Ray っぽい」
    /// Interactor で、Poke Interactor はそのままにする）。
    /// </summary>
    public sealed class CookingModeInputGate : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("調理モードで無効にする Interactor（NearFarInteractor・XRRayInteractor 等）。Poke は含めない。")]
        private Behaviour[] _rayLikeInteractors = System.Array.Empty<Behaviour>();

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
            var rayEnabled = mode == HandInputMode.PlacementMode;
            foreach (var interactor in _rayLikeInteractors)
            {
                if (interactor != null)
                {
                    interactor.enabled = rayEnabled;
                }
            }
        }
    }
}
