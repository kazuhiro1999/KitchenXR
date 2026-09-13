using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 領域を「対角の角から角までを指でつまむ」で描く。
    ///
    /// レイは使いません。始点も終点も**つまんだ手の位置そのもの**
    /// （<see cref="IPinchSource"/>＝人差し指と親指の中点。取れなければポークの指先）です。
    /// レイの当たり点だと、コンロには当たる物が無く（AR の平面はコンロを知らない）
    /// 矩形がどこにも出ませんでした。
    ///
    /// 始点を置いた高さに水平な面を張り、以後の手の高さは無視します（＝必ず水平な長方形）。
    /// 天板の高さは <see cref="HazardZones.AdjustHeight"/> の ±5cm で後から直せます。
    ///
    /// **始点と終点は別々のピンチでもよい**のが肝心——コンロは壁際で、対角の角まで手を
    /// 伸ばしたままにはできません。1回目のピンチで手前の角、離して身体を移し、2回目の
    /// ピンチで奥の角。1回のピンチで対角まで引いてもよい（引いた先が十分大きければ
    /// 離した時点で確定、小さければ「まだ終点待ち」として2回目のピンチを待ちます）。
    ///
    /// 待っている間に <see cref="TimeoutSeconds"/> 秒ピンチが無ければやめます——
    /// 「囲む」を押したことを忘れた手が、別の用でつまんだ拍子に矩形を作らないように。
    /// </summary>
    public sealed class HazardZoneDrawing : MonoBehaviour
    {
        /// <summary>ピンチを待つ上限（秒）。過ぎたらやめる。</summary>
        public const float DefaultTimeoutSeconds = 20f;

        /// <summary>ピンチを待つ上限（秒）。試験だけ短くする。</summary>
        public float TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

        /// <summary>作図の段。</summary>
        public enum Phase
        {
            /// <summary>描いていない。</summary>
            Off,

            /// <summary>始点を置くピンチ待ち。</summary>
            WaitingStart,

            /// <summary>つまんだまま終点を引いている。</summary>
            Drawing,

            /// <summary>始点は置いた。終点を置くピンチ待ち。</summary>
            WaitingEnd,
        }

        [SerializeField]
        [Tooltip("囲んでいる間だけ掴みを止める Interactor（rig の Near-Far Interactor 4つ）。"
                 + "手が追えないときの select 入力もここから取る。")]
        private XRBaseInputInteractor[] _interactors = Array.Empty<XRBaseInputInteractor>();

        [SerializeField]
        [Tooltip("手が追えないときの指先（rig の Poke Interactor）。")]
        private XRPokeInteractor[] _pokeInteractors = Array.Empty<XRPokeInteractor>();

        [SerializeField]
        [Tooltip("XR Origin。XR Hands の関節は追跡原点基準なので、世界へ出すのに要る。")]
        private Transform _originTransform;

        [SerializeField]
        [Tooltip("矩形の向き（yaw）の基準。未指定なら Camera.main。")]
        private Transform _headTransform;

        private IPinchSource _source;
        private Phase _phase = Phase.Off;
        private bool _wasPinching;
        private bool _secondPinch;
        private float _waitingSince;

        /// <summary>矩形が確定した（始点・終点・yaw）。</summary>
        public event Action<Vector3, Vector3, float> Committed;

        /// <summary>始点／終点が動いた（仮の枠を描き替える契機）。</summary>
        public event Action Progress;

        /// <summary>ピンチが無いまま時間切れになった。</summary>
        public event Action TimedOut;

        public Phase CurrentPhase => _phase;

        /// <summary>次のピンチを待っているか（始点待ちでも終点待ちでも）。</summary>
        public bool IsArmed => _phase == Phase.WaitingStart || _phase == Phase.WaitingEnd;

        /// <summary>今つまんだまま引いている最中か。</summary>
        public bool IsDragging => _phase == Phase.Drawing;

        /// <summary>始点が置かれているか（点を出す契機）。</summary>
        public bool HasStart => _phase == Phase.Drawing || _phase == Phase.WaitingEnd;

        /// <summary>始点（世界）。</summary>
        public Vector3 Start { get; private set; }

        /// <summary>今の終点（世界）。</summary>
        public Vector3 Current { get; private set; }

        /// <summary>始点の高さに張った水平面（世界の y）。</summary>
        public float PlaneY { get; private set; }

        /// <summary>矩形の向き（度）。始点を置いたときの頭の向き。</summary>
        public float YawDegrees { get; private set; }

        /// <summary>今の始点と終点で領域になる大きさか（＝離して確定してよいか）。</summary>
        public bool IsLargeEnough => HasStart && HazardZone.IsLargeEnough(Start, Current, YawDegrees);

        public void Bind(
            XRBaseInputInteractor[] interactors, Transform head = null,
            XRPokeInteractor[] pokes = null, Transform origin = null)
        {
            _interactors = interactors ?? Array.Empty<XRBaseInputInteractor>();

            if (head != null)
            {
                _headTransform = head;
            }

            if (pokes != null)
            {
                _pokeInteractors = pokes;
            }

            if (origin != null)
            {
                _originTransform = origin;
            }

            _source = null; // 次に読むときに組み直す。
        }

        /// <summary>ピンチの出どころを差し替える（試験と Editor の試し）。</summary>
        public void SetPinchSource(IPinchSource source) => _source = source;

        /// <summary>今のピンチの出どころ。無ければ実機用を組む。</summary>
        public IPinchSource Source =>
            _source ??= new HandPinchSource(_originTransform, _pokeInteractors, _interactors);

        /// <summary>「囲む」——次のピンチで始点を取る。</summary>
        public void Arm()
        {
            _phase = Phase.WaitingStart;
            _secondPinch = false;

            // 押した手がもうつまんでいても始点にしない（離してからの1回を待つ）。
            _wasPinching = true;
            _waitingSince = Time.time;

            SetGrabSuspended(true);
        }

        /// <summary>「やり直す」「戻る」「時間切れ」——描くのをやめる。</summary>
        public void Disarm()
        {
            _phase = Phase.Off;
            _secondPinch = false;
            SetGrabSuspended(false);
        }

        /// <summary>
        /// 描いている間だけレイの掴みを止める（触れてよい層を空にする）。
        ///
        /// 配置モードのレイは全ての層に届くので、囲もうとしたピンチが視線の先の板を
        /// 掴んでしまう（板が飛んで、矩形も始まらない）。
        /// </summary>
        private void SetGrabSuspended(bool suspended)
        {
            foreach (var interactor in _interactors)
            {
                if (interactor != null)
                {
                    interactor.interactionLayers =
                        suspended ? 0 : CookingModeInputGate.PlacementRayInteractionLayers;
                }
            }
        }

        private void Update()
        {
            if (_phase == Phase.Off)
            {
                return;
            }

            var sample = Source.Read();
            var pinching = sample.IsTracked && sample.IsPinching;
            var pressed = pinching && !_wasPinching;
            _wasPinching = pinching;

            switch (_phase)
            {
                case Phase.WaitingStart:
                    if (pressed)
                    {
                        BeginAt(sample.Position);
                    }
                    else
                    {
                        CheckTimeout();
                    }

                    break;

                case Phase.WaitingEnd:
                    if (pressed)
                    {
                        ResumeAt(sample.Position);
                    }
                    else
                    {
                        CheckTimeout();
                    }

                    break;

                case Phase.Drawing:
                    if (pinching)
                    {
                        DragTo(sample.Position);
                    }
                    else
                    {
                        ReleasePinch();
                    }

                    break;
            }
        }

        private void CheckTimeout()
        {
            if (Time.time - _waitingSince < TimeoutSeconds)
            {
                return;
            }

            Disarm();
            TimedOut?.Invoke();
        }

        // ---------------------------------------------------------------- 段を移す4つ

        /// <summary>始点を置く（＝その高さに水平な面を張る）。</summary>
        public void BeginAt(Vector3 point)
        {
            PlaneY = point.y;
            Start = point;
            Current = point;
            YawDegrees = HeadYaw();
            _phase = Phase.Drawing;
            _secondPinch = false;
            Progress?.Invoke();
        }

        /// <summary>終点を動かす。高さは始点の面に貼り付ける（＝必ず水平な長方形）。</summary>
        public void DragTo(Vector3 point)
        {
            if (_phase != Phase.Drawing)
            {
                return;
            }

            point.y = PlaneY;
            Current = point;
            Progress?.Invoke();
        }

        /// <summary>2回目のピンチ——終点を置き直して、そのまま引ける段へ。</summary>
        public void ResumeAt(Vector3 point)
        {
            if (_phase != Phase.WaitingEnd)
            {
                return;
            }

            _secondPinch = true;
            _phase = Phase.Drawing;
            DragTo(point);
        }

        /// <summary>
        /// 指を離した。十分な大きさなら確定、まだ小さければ**終点待ち**へ——
        /// 「つまんで、離して、離れた角でもう一度つまむ」を成り立たせるため。
        /// 2回目のピンチのあとは小さくても確定する（待ち続けない）。
        /// </summary>
        public void ReleasePinch()
        {
            if (_phase != Phase.Drawing)
            {
                return;
            }

            if (_secondPinch || IsLargeEnough)
            {
                Commit();
                return;
            }

            _phase = Phase.WaitingEnd;
            _waitingSince = Time.time;
            Progress?.Invoke();
        }

        /// <summary>矩形を確定する。</summary>
        public void Commit()
        {
            if (!HasStart)
            {
                return;
            }

            _phase = Phase.Off;
            _secondPinch = false;
            SetGrabSuspended(false);
            Committed?.Invoke(Start, Current, YawDegrees);
        }

        // ---------------------------------------------------------------- 頭の向き

        private Transform Head =>
            _headTransform != null ? _headTransform : Camera.main != null ? Camera.main.transform : null;

        /// <summary>
        /// 矩形の向きは始点を置いたときの頭の向き。コンロに向かって囲めば矩形が天板の縁に
        /// 沿うので、床の線が部屋の座標系と斜めに交わって見えない。
        /// </summary>
        private float HeadYaw()
        {
            var head = Head;
            if (head == null)
            {
                return 0f;
            }

            var forward = head.forward;
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-6f
                ? 0f
                : Quaternion.LookRotation(forward.normalized, Vector3.up).eulerAngles.y;
        }
    }
}
