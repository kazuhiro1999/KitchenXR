using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 領域を「辺 → 奥行き」の2段階で描く。
    ///
    /// レイは使いません。位置は全て**つまんだ手の位置そのもの**
    /// （<see cref="IPinchSource"/>＝人差し指と親指の中点。取れなければポークの指先）です。
    /// レイの当たり点だと、コンロには当たる物が無く（AR の平面はコンロを知らない）
    /// 矩形がどこにも出ませんでした。
    ///
    /// 1. **辺**——1回目のピンチ開始が点 A。つまんでいる間、A から手の位置まで
    ///    A の高さで水平な線をリアルタイムに引く。離した位置が点 B。A→B が手前の辺で、
    ///    **これで矩形の向き（yaw）が決まる**。
    /// 2. **奥行き**——2回目のピンチ開始から、手を辺 AB に直角な向きへ射影した距離を
    ///    奥行きにして、A・B・その奥行きの長方形をリアルタイムに出す。離した位置で確定。
    ///    奥行きの向きは手のある側（正負どちらでも）。
    ///
    /// 対角の2点では向きが決まらない（同じ2点を通る矩形が無数にあり、実機では斜めに
    /// 転びました）のが2段階にした理由です。
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

            /// <summary>手前の辺の端（点 A）を置くピンチ待ち。</summary>
            WaitingEdge,

            /// <summary>つまんだまま手前の辺を引いている。</summary>
            DrawingEdge,

            /// <summary>辺は決まった。奥行きを引くピンチ待ち。</summary>
            WaitingDepth,

            /// <summary>つまんだまま奥行きを引いている。</summary>
            DrawingDepth,
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

        private IPinchSource _source;
        private Phase _phase = Phase.Off;
        private bool _wasPinching;
        private float _waitingSince;

        /// <summary>矩形が確定した（辺の端 A・端 B・符号付きの奥行き）。</summary>
        public event Action<Vector3, Vector3, float> Committed;

        /// <summary>手前の辺が決まった（＝奥行きの段に入った）。札の書き換えの契機。</summary>
        public event Action EdgeFixed;

        /// <summary>辺か奥行きが短すぎて確定できなかった。同じ段をもう一度待つ。</summary>
        public event Action TooSmall;

        /// <summary>描いている形が動いた（仮の枠を描き替える契機）。</summary>
        public event Action Progress;

        /// <summary>ピンチが無いまま時間切れになった。</summary>
        public event Action TimedOut;

        public Phase CurrentPhase => _phase;

        /// <summary>次のピンチを待っているか（辺の待ちでも奥行きの待ちでも）。</summary>
        public bool IsArmed => _phase == Phase.WaitingEdge || _phase == Phase.WaitingDepth;

        /// <summary>今つまんだまま引いている最中か。</summary>
        public bool IsDragging => _phase == Phase.DrawingEdge || _phase == Phase.DrawingDepth;

        /// <summary>点 A が置かれているか（点を出す契機）。</summary>
        public bool HasEdgeStart =>
            _phase == Phase.DrawingEdge || _phase == Phase.WaitingDepth || _phase == Phase.DrawingDepth;

        /// <summary>手前の辺が決まっているか（＝奥行きの段）。</summary>
        public bool HasEdge => _phase == Phase.WaitingDepth || _phase == Phase.DrawingDepth;

        /// <summary>手前の辺の端 A（世界）。</summary>
        public Vector3 EdgeStart { get; private set; }

        /// <summary>手前の辺の端 B（世界）。辺の段では今の手の位置。</summary>
        public Vector3 EdgeEnd { get; private set; }

        /// <summary>今の手の位置を面へ落としたもの（世界）。</summary>
        public Vector3 Current { get; private set; }

        /// <summary>符号付きの奥行き（m）。辺に直角な向きへの射影。</summary>
        public float Depth { get; private set; }

        /// <summary>点 A の高さに張った水平面（世界の y）。</summary>
        public float PlaneY { get; private set; }

        /// <summary>矩形の向き（度）。手前の辺 A→B の向き。</summary>
        public float YawDegrees { get; private set; }

        /// <summary>手前の辺の長さ（m）。</summary>
        public float EdgeLength => HasEdgeStart ? HazardZone.EdgeLength(EdgeStart, EdgeEnd) : 0f;

        public void Bind(
            XRBaseInputInteractor[] interactors,
            XRPokeInteractor[] pokes = null, Transform origin = null)
        {
            _interactors = interactors ?? Array.Empty<XRBaseInputInteractor>();

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

        /// <summary>「囲む」——次のピンチで手前の辺を引き始める。</summary>
        public void Arm()
        {
            _phase = Phase.WaitingEdge;

            // 押した手がもうつまんでいても始点にしない（離してからの1回を待つ）。
            _wasPinching = true;
            _waitingSince = Time.time;

            SetGrabSuspended(true);
        }

        /// <summary>「やり直す」「戻る」「時間切れ」——描くのをやめる。</summary>
        public void Disarm()
        {
            _phase = Phase.Off;
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
                case Phase.WaitingEdge:
                    if (pressed)
                    {
                        BeginEdgeAt(sample.Position);
                    }
                    else
                    {
                        CheckTimeout();
                    }

                    break;

                case Phase.DrawingEdge:
                    if (pinching)
                    {
                        DragEdgeTo(sample.Position);
                    }
                    else
                    {
                        ReleaseEdge();
                    }

                    break;

                case Phase.WaitingDepth:
                    if (pressed)
                    {
                        BeginDepthAt(sample.Position);
                    }
                    else
                    {
                        CheckTimeout();
                    }

                    break;

                case Phase.DrawingDepth:
                    if (pinching)
                    {
                        DragDepthTo(sample.Position);
                    }
                    else
                    {
                        ReleaseDepth();
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

        // ---------------------------------------------------------------- 1段目: 手前の辺

        /// <summary>点 A を置く（＝その高さに水平な面を張る）。</summary>
        public void BeginEdgeAt(Vector3 point)
        {
            PlaneY = point.y;
            EdgeStart = point;
            EdgeEnd = point;
            Current = point;
            YawDegrees = 0f;
            Depth = 0f;
            _phase = Phase.DrawingEdge;
            Progress?.Invoke();
        }

        /// <summary>辺の端 B を動かす。高さは A の面に貼り付ける（＝必ず水平な辺）。</summary>
        public void DragEdgeTo(Vector3 point)
        {
            if (_phase != Phase.DrawingEdge)
            {
                return;
            }

            point.y = PlaneY;
            EdgeEnd = point;
            Current = point;
            YawDegrees = HazardZone.YawFromEdge(EdgeStart, EdgeEnd);
            Progress?.Invoke();
        }

        /// <summary>辺を引き終えて離した。十分な長さなら奥行きの段へ、短すぎれば引き直し。</summary>
        public void ReleaseEdge()
        {
            if (_phase != Phase.DrawingEdge)
            {
                return;
            }

            if (HazardZone.EdgeLength(EdgeStart, EdgeEnd) < HazardZone.MinSideMeters)
            {
                _phase = Phase.WaitingEdge;
                _waitingSince = Time.time;
                Progress?.Invoke();
                TooSmall?.Invoke();
                return;
            }

            _phase = Phase.WaitingDepth;
            _waitingSince = Time.time;
            Progress?.Invoke();
            EdgeFixed?.Invoke();
        }

        // ---------------------------------------------------------------- 2段目: 奥行き

        /// <summary>2回目のピンチ——奥行きを引き始める。</summary>
        public void BeginDepthAt(Vector3 point)
        {
            if (_phase != Phase.WaitingDepth)
            {
                return;
            }

            _phase = Phase.DrawingDepth;
            DragDepthTo(point);
        }

        /// <summary>手を辺に直角な向きへ射影して奥行きにする（符号はそのまま＝手のある側）。</summary>
        public void DragDepthTo(Vector3 point)
        {
            if (_phase != Phase.DrawingDepth)
            {
                return;
            }

            point.y = PlaneY;
            Current = point;
            Depth = Vector3.Dot(point - EdgeStart, HazardZone.DepthAxis(YawDegrees));
            Progress?.Invoke();
        }

        /// <summary>奥行きを離した。十分な深さなら確定、浅すぎれば引き直し。</summary>
        public void ReleaseDepth()
        {
            if (_phase != Phase.DrawingDepth)
            {
                return;
            }

            if (Mathf.Abs(Depth) < HazardZone.MinSideMeters)
            {
                _phase = Phase.WaitingDepth;
                _waitingSince = Time.time;
                Progress?.Invoke();
                TooSmall?.Invoke();
                return;
            }

            Commit();
        }

        /// <summary>矩形を確定する。</summary>
        public void Commit()
        {
            if (!HasEdge)
            {
                return;
            }

            _phase = Phase.Off;
            SetGrabSuspended(false);
            Committed?.Invoke(EdgeStart, EdgeEnd, Depth);
        }
    }
}
