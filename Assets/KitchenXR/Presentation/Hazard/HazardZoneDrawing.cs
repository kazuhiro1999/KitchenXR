using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 領域を「レイで指してピンチしたまま水平にドラッグして囲む」で描く。
    ///
    /// コンロは壁際にあって手が届かないので、床の平面や物の面を直接なぞらせることはできない。
    /// そこで**最初のピンチの当たり点の高さに水平な作業面を仮に張り**、以後はレイとその面との
    /// 交点だけを追う（＝手を伸ばさずに天板の上を囲める）。面は無限平面なので、交点は
    /// <c>t = (planeY - o.y) / d.y</c> の1行で取れる——AR の平面検出には頼らない。
    ///
    /// ピンチは XRI の「選択」だが、<c>selectEntered</c>／<c>selectExited</c> は**何かを
    /// 選んだときだけ**出る。空を指してピンチしても発火しないので、掴んだ相手に関わらず
    /// 押されていることが分かる <c>logicalSelectState</c>（<c>wasPerformedThisFrame</c>／
    /// <c>isPerformed</c>／<c>wasCompletedThisFrame</c>）を見る。
    ///
    /// レイの出どころは <see cref="IXRRayProvider.GetOrCreateRayOrigin"/>——手でも
    /// コントローラでも、Near-Far Interactor でも Ray Interactor でも同じ口で取れる。
    /// </summary>
    public sealed class HazardZoneDrawing : MonoBehaviour
    {
        /// <summary>当たる物が無いときの作業面の高さ（頭からの下がり。m）。一般的な天板の高さ。</summary>
        public const float FallbackBelowHeadMeters = 0.30f;

        /// <summary>レイを伸ばす上限（m）。これより遠い交点は台所の外なので捨てる。</summary>
        public const float MaxReachMeters = 6f;

        /// <summary>水平に近すぎるレイは作業面と交わらない（|d.y| の下限）。</summary>
        private const float MinVerticalComponent = 0.05f;

        /// <summary>一度に見る当たりの数。台所の板は数枚なので余裕を持って 16。</summary>
        private const int MaxHits = 16;

        private readonly RaycastHit[] _hits = new RaycastHit[MaxHits];

        [SerializeField]
        [Tooltip("レイを持つ Interactor（rig の Near-Far Interactor 4つ）。")]
        private XRBaseInputInteractor[] _interactors = Array.Empty<XRBaseInputInteractor>();

        [SerializeField]
        [Tooltip("矩形の向き（yaw）と、当たる物が無いときの高さの基準。未指定なら Camera.main。")]
        private Transform _headTransform;

        /// <summary>矩形が確定した（始点・終点・yaw・床の高さは呼び出し側が決める）。</summary>
        public event Action<Vector3, Vector3, float> Committed;

        /// <summary>伸びている途中（仮の枠を描き替える契機）。</summary>
        public event Action Progress;

        /// <summary>次のピンチを待っているか。</summary>
        public bool IsArmed { get; private set; }

        /// <summary>今ドラッグの最中か。</summary>
        public bool IsDragging { get; private set; }

        /// <summary>始点（世界）。</summary>
        public Vector3 Start { get; private set; }

        /// <summary>今の終点（世界）。</summary>
        public Vector3 Current { get; private set; }

        /// <summary>仮の作業面の高さ（世界の y）。</summary>
        public float PlaneY { get; private set; }

        /// <summary>矩形の向き（度）。囲み始めたときの頭の向き。</summary>
        public float YawDegrees { get; private set; }

        public void Bind(XRBaseInputInteractor[] interactors, Transform head = null)
        {
            _interactors = interactors ?? Array.Empty<XRBaseInputInteractor>();
            if (head != null)
            {
                _headTransform = head;
            }
        }

        /// <summary>「囲む」——次のピンチで始点を取る。</summary>
        public void Arm()
        {
            IsArmed = true;
            IsDragging = false;
            SetGrabSuspended(true);
        }

        /// <summary>「やり直す」「戻る」——描くのをやめる。</summary>
        public void Disarm()
        {
            IsArmed = false;
            IsDragging = false;
            SetGrabSuspended(false);
        }

        /// <summary>
        /// 描いている間だけレイの掴みを止める（触れてよい層を空にする）。
        ///
        /// 配置モードのレイは全ての層に届くので、コンロを指したレイが途中の板を横切っていると、
        /// 囲もうとしたピンチがその板を掴んでしまう（板が飛んで、矩形も始まらない）。
        /// 戻す先が配置モードの値で固定なのは、描くのが配置モードの中だけだから。
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
            if (!IsArmed && !IsDragging)
            {
                return;
            }

            foreach (var interactor in _interactors)
            {
                if (interactor == null || !interactor.isActiveAndEnabled)
                {
                    continue;
                }

                var state = interactor.logicalSelectState;
                if (state == null)
                {
                    continue;
                }

                if (!IsDragging)
                {
                    if (state.wasPerformedThisFrame && TryGetRay(interactor, out var startRay))
                    {
                        BeginAt(FirstHitPoint(startRay));
                    }

                    continue;
                }

                if (TryGetRay(interactor, out var ray) && TryIntersectPlane(ray, PlaneY, out var point))
                {
                    DragTo(point);
                }

                if (state.wasCompletedThisFrame || !state.isPerformed)
                {
                    Commit();
                }

                return;
            }
        }

        // ---------------------------------------------------------------- 試験から叩ける3つ

        /// <summary>始点を置く（＝仮の作業面をその高さに張る）。</summary>
        public void BeginAt(Vector3 point)
        {
            PlaneY = point.y;
            Start = point;
            Current = point;
            YawDegrees = HeadYaw();
            IsArmed = false;
            IsDragging = true;
            Progress?.Invoke();
        }

        /// <summary>終点を動かす（水平にドラッグ）。高さは作業面に貼り付ける。</summary>
        public void DragTo(Vector3 point)
        {
            if (!IsDragging)
            {
                return;
            }

            point.y = PlaneY;
            Current = point;
            Progress?.Invoke();
        }

        /// <summary>離した——矩形を確定する。</summary>
        public void Commit()
        {
            if (!IsDragging)
            {
                return;
            }

            IsDragging = false;
            SetGrabSuspended(false);
            Committed?.Invoke(Start, Current, YawDegrees);
        }

        // ---------------------------------------------------------------- レイと面

        /// <summary>
        /// 最初のピンチの当たり点。台所の物（AR の面・壁・家具）に当たればその点、
        /// 当たらなければ「頭の高さ − 30cm」の水平面との交点
        /// （それも取れなければレイの 1m 先）。
        ///
        /// **板は数えない。** レイが途中の板を横切っていると、作業面が板の面の高さに張られて
        /// しまう（板は胸の高さなので、天板よりずっと高いところを囲うことになる）。
        /// </summary>
        private Vector3 FirstHitPoint(Ray ray)
        {
            var count = Physics.RaycastNonAlloc(
                ray, _hits, MaxReachMeters, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            var nearest = float.PositiveInfinity;
            var found = false;
            var point = Vector3.zero;

            for (var i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || hit.collider.GetComponentInParent<XRBaseInteractable>() != null)
                {
                    continue; // 板（XRI の相手）は飛ばす。
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    point = hit.point;
                    found = true;
                }
            }

            if (found)
            {
                return point;
            }

            var fallbackY = HeadPosition().y - FallbackBelowHeadMeters;
            return TryIntersectPlane(ray, fallbackY, out var fallback)
                ? fallback
                : ray.origin + ray.direction;
        }

        /// <summary>レイと高さ <paramref name="planeY"/> の水平な無限平面との交点。</summary>
        public static bool TryIntersectPlane(Ray ray, float planeY, out Vector3 point)
        {
            point = default;

            var dy = ray.direction.y;
            if (Mathf.Abs(dy) < MinVerticalComponent)
            {
                return false; // 水平に近いレイは面と交わらない（交点が無限遠へ飛ぶ）。
            }

            var t = (planeY - ray.origin.y) / dy;
            if (t <= 0f || t > MaxReachMeters)
            {
                return false;
            }

            point = ray.origin + ray.direction * t;
            return true;
        }

        private static bool TryGetRay(XRBaseInputInteractor interactor, out Ray ray)
        {
            ray = default;

            if (interactor is not IXRRayProvider provider)
            {
                return false;
            }

            var origin = provider.GetOrCreateRayOrigin();
            if (origin == null)
            {
                return false;
            }

            ray = new Ray(origin.position, origin.forward);
            return true;
        }

        private Transform Head =>
            _headTransform != null ? _headTransform : Camera.main != null ? Camera.main.transform : null;

        private Vector3 HeadPosition()
        {
            var head = Head;
            return head != null ? head.position : Vector3.zero;
        }

        /// <summary>
        /// 矩形の向きは囲み始めたときの頭の向き。コンロに向かって囲めば矩形が天板の縁に
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
