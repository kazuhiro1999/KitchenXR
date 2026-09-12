using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 注意の板の「消す」だけを持つ小さな板。注意の板の右横に、配置モードの間だけ出る。
    ///
    /// 別の板にしてあるのは、配置モードの注意の板が掴む相手（<c>XRGrabInteractable</c>）になり、
    /// 同じコライダーに押せる釦を載せると XRI の引き当てが掴みと押しで奪い合うため。
    /// 配線を <c>OnEnable</c> のたびにやり直すのは <see cref="PlacementMenuPanel"/> と同じ理由
    /// （<see cref="UIDocument"/> は無効化のたびに <c>rootVisualElement</c> を作り直す）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HazardDeleteChip : MonoBehaviour
    {
        /// <summary>2度押しの猶予（秒）。他の2度押しと同じ長さ。</summary>
        public const float ConfirmSeconds = 4f;

        private const string Label = "消す";
        private const string ArmedLabel = "もう一度";
        private const string ArmedClass = "hazard-delete-button--armed";

        private readonly ClickDebounce _debounce = new ClickDebounce();

        /// <summary>2度押された（＝本当に消す）。</summary>
        public event Action Confirmed;

        private TwoPressButton _press;
        private bool _bound;

        /// <summary>2度目を待っているか（試験用）。</summary>
        public bool IsArmed => _press != null && _press.IsArmed;

        private void OnEnable()
        {
            _bound = false;
            TryBind();
        }

        private void OnDisable() => _press?.Disarm();

        private void Update()
        {
            if (!_bound)
            {
                TryBind();
                return;
            }

            _press?.Tick();
        }

        private void TryBind()
        {
            var document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            var button = root?.Q<Button>("deleteButton");
            if (button == null)
            {
                return;
            }

            _press = new TwoPressButton(button, Label, ArmedLabel, ConfirmSeconds, ArmedClass);
            _press.Confirmed += () => Confirmed?.Invoke();
            PokePress.BindButton(button, _debounce, "hazardDelete", () => _press.Press());

            _bound = true;
        }

        /// <summary>試験から2度押しを起こす（指を動かさずに確かめるため）。</summary>
        public void PressForTest()
        {
            if (!_bound)
            {
                TryBind();
            }

            _press?.Press();
        }
    }
}
