using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 実機のピンチ。**手の位置そのもの**を返す（レイの当たり点ではない）。
    ///
    /// 1本目は XR Hands（<see cref="XRHandSubsystem"/>）——<see cref="XRHandJointID.IndexTip"/> と
    /// <see cref="XRHandJointID.ThumbTip"/> の中点が位置、2指の間隔が
    /// <see cref="PinchMeters"/> 未満ならつまんでいる。関節の Pose は**追跡原点（XR Origin）基準**
    /// なので、世界へ出すには XR Origin を通す。
    ///
    /// 2本目は rig の select 入力（<see cref="XRBaseInputInteractor.logicalSelectState"/>）。
    /// 手が追えない／XR Hands が動いていない端末でもピンチは取れるので、そのときの位置は
    /// <see cref="XRPokeInteractor"/> の attach（＝人差し指の先）から借りる。同じ手の 2 つの
    /// Interactor は数 cm しか離れていないので、**一番近いポーク**を選べば左右を取り違えない。
    ///
    /// つまむ／離すの閾値をずらしてあるのは、2cm の境目で指が震えると矩形が確定と再開を
    /// 繰り返すため（<see cref="ReleaseMeters"/>）。
    /// </summary>
    public sealed class HandPinchSource : IPinchSource
    {
        /// <summary>つまんだと見なす2指の間隔（m）。</summary>
        public const float PinchMeters = 0.02f;

        /// <summary>離したと見なす間隔（m）。つまむ側より広い＝ヒステリシス。</summary>
        public const float ReleaseMeters = 0.035f;

        private static readonly List<XRHandSubsystem> Found = new List<XRHandSubsystem>();

        private readonly Transform _origin;
        private readonly XRPokeInteractor[] _pokes;
        private readonly XRBaseInputInteractor[] _selects;

        private XRHandSubsystem _hands;
        private bool _pinching;

        /// <summary>直前のフレームで XR Hands から取れたか（札に出して実機で切り分ける）。</summary>
        public bool UsingHandJoints { get; private set; }

        public HandPinchSource(
            Transform origin, XRPokeInteractor[] pokes, XRBaseInputInteractor[] selects)
        {
            _origin = origin;
            _pokes = pokes ?? Array.Empty<XRPokeInteractor>();
            _selects = selects ?? Array.Empty<XRBaseInputInteractor>();
        }

        public PinchSample Read()
        {
            if (TryReadHands(out var sample))
            {
                UsingHandJoints = true;
                return sample;
            }

            UsingHandJoints = false;
            return ReadSelectInput();
        }

        // ---------------------------------------------------------------- XR Hands

        private bool TryReadHands(out PinchSample sample)
        {
            sample = PinchSample.None;

            var subsystem = Subsystem;
            if (subsystem == null)
            {
                return false;
            }

            var hasLeft = TryReadHand(subsystem.leftHand, out var leftPoint, out var leftGap);
            var hasRight = TryReadHand(subsystem.rightHand, out var rightPoint, out var rightGap);

            if (!hasLeft && !hasRight)
            {
                return false;
            }

            // つまんでいる方を優先。両手なら間隔の狭い方（意図してつまんでいる方）。
            var point = leftPoint;
            var gap = leftGap;
            if (!hasLeft || (hasRight && rightGap < leftGap))
            {
                point = rightPoint;
                gap = rightGap;
            }

            _pinching = _pinching ? gap < ReleaseMeters : gap < PinchMeters;
            sample = new PinchSample(true, point, _pinching);
            return true;
        }

        private bool TryReadHand(XRHand hand, out Vector3 position, out float gap)
        {
            position = default;
            gap = float.PositiveInfinity;

            if (!hand.isTracked ||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index) ||
                !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb))
            {
                return false;
            }

            gap = Vector3.Distance(index.position, thumb.position);

            var middle = (index.position + thumb.position) * 0.5f;
            position = _origin != null ? _origin.TransformPoint(middle) : middle;
            return true;
        }

        private XRHandSubsystem Subsystem
        {
            get
            {
                if (_hands != null && _hands.running)
                {
                    return _hands;
                }

                _hands = null;
                Found.Clear();
                SubsystemManager.GetSubsystems(Found);

                foreach (var candidate in Found)
                {
                    if (candidate != null && candidate.running)
                    {
                        _hands = candidate;
                        break;
                    }
                }

                return _hands;
            }
        }

        // ---------------------------------------------------------------- rig の select

        private PinchSample ReadSelectInput()
        {
            foreach (var interactor in _selects)
            {
                if (interactor == null || !interactor.isActiveAndEnabled)
                {
                    continue;
                }

                var state = interactor.logicalSelectState;
                if (state == null || !state.isPerformed)
                {
                    continue;
                }

                if (TryNearestFingertip(interactor.transform.position, out var fingertip))
                {
                    return new PinchSample(true, fingertip, true);
                }
            }

            // つまんでいない間は位置も返さない——使うのは始点／終点を置く瞬間だけ。
            return PinchSample.None;
        }

        private bool TryNearestFingertip(Vector3 near, out Vector3 position)
        {
            position = default;

            var best = float.PositiveInfinity;
            foreach (var poke in _pokes)
            {
                if (poke == null || !poke.isActiveAndEnabled)
                {
                    continue;
                }

                var attach = poke.GetAttachTransform(null);
                if (attach == null)
                {
                    continue;
                }

                var distance = Vector3.SqrMagnitude(attach.position - near);
                if (distance < best)
                {
                    best = distance;
                    position = attach.position;
                }
            }

            return best < float.PositiveInfinity;
        }
    }
}
