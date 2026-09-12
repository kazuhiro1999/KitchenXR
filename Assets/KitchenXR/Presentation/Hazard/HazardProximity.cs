using System;
using KitchenXR.Platform;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 手と頭が領域へ近づいたかを見張って、床の線の色と音を切り替える。
    ///
    /// 見るのは 10Hz で足りる（調査 §4.3）——手は 1 秒で 1m も動かないし、毎フレーム測っても
    /// 段の変わり目が早くなるだけで見え方は変わらない。手の位置は Poke Interactor の
    /// 指先（<c>GetAttachTransform</c>）を使う: XR Hands の関節を直に読むと、コントローラを
    /// 持っているときに何も取れなくなる。
    ///
    /// **カメラも認識も使わない。** 領域は手で囲ったものなので、カメラが落ちても警告は出る
    /// （調査 §3.3 の 4 番）。配置モードの間は離れていても線を薄く出す——領域が見えないと
    /// 置き直せない。
    /// </summary>
    public sealed class HazardProximity : MonoBehaviour
    {
        /// <summary>見張る間隔（秒）。10Hz。</summary>
        public const float IntervalSeconds = 0.1f;

        [SerializeField]
        [Tooltip("領域の一式（同じ根に載せる）。")]
        private HazardZones _zones;

        [SerializeField]
        [Tooltip("注意の音（同じ根に載せる）。")]
        private HazardSound _sound;

        [SerializeField]
        [Tooltip("手の位置を取る Poke Interactor（左右の手・左右のコントローラ）。")]
        private XRPokeInteractor[] _hands = Array.Empty<XRPokeInteractor>();

        [SerializeField]
        [Tooltip("頭の Transform。未指定なら Camera.main。")]
        private Transform _headTransform;

        private IHandInputPolicy _policy;
        private float _nextCheck;

        public void Bind(
            HazardZones zones, HazardSound sound, XRPokeInteractor[] hands,
            Transform head = null, IHandInputPolicy policy = null)
        {
            if (zones != null)
            {
                _zones = zones;
            }

            if (sound != null)
            {
                _sound = sound;
            }

            // null は「シーンで挿したものを使う」。空の配列で上書きすると手が取れなくなる。
            if (hands != null)
            {
                _hands = hands;
            }

            if (head != null)
            {
                _headTransform = head;
            }

            _policy = policy;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck)
            {
                return;
            }

            _nextCheck = Time.unscaledTime + IntervalSeconds;
            Evaluate();
        }

        /// <summary>1回分の見張り（試験がそのまま呼べるように分けてある）。</summary>
        public void Evaluate()
        {
            if (_zones == null)
            {
                return;
            }

            // 配置モードの間は、離れていても薄く出しておく。
            var placing = _policy != null && _policy.CurrentMode == HandInputMode.PlacementMode;
            var head = HeadPosition();

            foreach (var entry in _zones.Entries)
            {
                var hand = NearestHandDistance(entry.Zone);
                var headDistance = head.HasValue ? entry.Zone.DistanceTo(head.Value) : float.PositiveInfinity;

                var level = entry.Alert.Evaluate(hand, headDistance);
                entry.Visual?.SetLevel(level, placing);

                if (entry.Alert.EnteredNear)
                {
                    _sound?.Play();
                }
            }
        }

        /// <summary>一番近い手（指先）から領域までの距離。手が1つも追えていなければ無限。</summary>
        private float NearestHandDistance(HazardZone zone)
        {
            var nearest = float.PositiveInfinity;

            foreach (var poke in _hands)
            {
                if (poke == null || !poke.isActiveAndEnabled)
                {
                    continue;
                }

                var tip = poke.GetAttachTransform(null);
                if (tip == null)
                {
                    continue;
                }

                var distance = zone.DistanceTo(tip.position);
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }

        private Vector3? HeadPosition()
        {
            var head = _headTransform != null
                ? _headTransform
                : Camera.main != null ? Camera.main.transform : null;

            return head != null ? head.position : (Vector3?)null;
        }
    }
}
