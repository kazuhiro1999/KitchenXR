using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 「2度押し」のボタン（P3 の「一覧へ」で決めた流儀を P2 の「配置」でも使う）。
    ///
    /// 1度目は身構えるだけ（文字が変わり色が付く）で、猶予の間にもう1度押されたときだけ本番。
    /// **長押しにしない**理由は「一覧へ」と同じ——ポークは押し下げで発火する（設計 §11 追補）ので、
    /// 手応えの無い板に指が深く入って留まる「普通の1回押し」と長押しが区別できない。
    ///
    /// 発火そのものは <see cref="PokePress"/> が受け、ここは「1度目か2度目か」だけを見る。
    /// </summary>
    public sealed class TwoPressButton
    {
        private readonly Button _button;
        private readonly string _label;
        private readonly string _armedLabel;
        private readonly string _armedClass;
        private readonly float _confirmSeconds;

        private float _armedUntil;

        public TwoPressButton(
            Button button, string label, string armedLabel, float confirmSeconds, string armedClass)
        {
            _button = button;
            _label = label;
            _armedLabel = armedLabel;
            _armedClass = armedClass;
            _confirmSeconds = confirmSeconds;

            Disarm();
        }

        /// <summary>2度目が押された（＝本番）。</summary>
        public event Action Confirmed;

        /// <summary>2度目を待っているか（試験用）。</summary>
        public bool IsArmed => _armedUntil > 0f && Time.unscaledTime <= _armedUntil;

        /// <summary>押されたときに呼ぶ。1度目は身構え、猶予内の2度目で <see cref="Confirmed"/>。</summary>
        public void Press()
        {
            if (IsArmed)
            {
                Disarm();
                Confirmed?.Invoke();
                return;
            }

            _armedUntil = Time.unscaledTime + _confirmSeconds;
            if (_button != null)
            {
                _button.text = _armedLabel;
                if (!string.IsNullOrEmpty(_armedClass))
                {
                    _button.AddToClassList(_armedClass);
                }
            }
        }

        /// <summary>身構えを解く（猶予切れ・別の意図の操作・板が入れ替わった）。</summary>
        public void Disarm()
        {
            _armedUntil = 0f;
            if (_button != null)
            {
                _button.text = _label;
                if (!string.IsNullOrEmpty(_armedClass))
                {
                    _button.RemoveFromClassList(_armedClass);
                }
            }
        }

        /// <summary>板の <c>Update</c> から呼ぶ。猶予が切れていれば黙って戻す。</summary>
        public void Tick()
        {
            if (_armedUntil > 0f && Time.unscaledTime > _armedUntil)
            {
                Disarm();
            }
        }
    }
}
