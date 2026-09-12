using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 人差し指の先に出す小さな光る点。ホログラムの板には手応えも影も無く、指と面の距離の
    /// 手掛かりが両眼視差しか無い（腕を伸ばすほど弱くなる）ので、面との距離で大きさが変わる点を
    /// 距離計にする。出すのは板に近づいたときだけ——常に出すと料理中の手に点が付いて回る。
    ///
    /// 近さに <see cref="XRPokeInteractor"/> の <c>hasHover</c> を使わないのは、あれが
    /// <c>pokeHoverRadius</c>（既定 1.5cm）の球の重なりで決まり「もう触れる」ところでしか
    /// 出ないため。自分で <see cref="FarDistanceMeters"/>（5cm）の球を撫でて距離を測る。
    /// <c>pokeHoverRadius</c> を広げる手は採らない——XRI のホバー（色・振動）まで 5cm 手前で
    /// 始まり、触っていない板が反応する。
    /// </summary>
    public sealed class FingertipCursor : MonoBehaviour
    {
        /// <summary>点の直径（m）。8mm——指先に載って視界を塞がない一番大きい値。</summary>
        public const float DotDiameterMeters = 0.008f;

        /// <summary>これより遠い板は無視する（m）。ここで点が一番小さく、面で一番大きくなる。</summary>
        public const float FarDistanceMeters = 0.05f;

        /// <summary>一番遠いときの倍率（＝ 8mm × 0.4 ≒ 3mm）。</summary>
        public const float MinScale = 0.4f;

        /// <summary>面に触れたときの倍率。</summary>
        public const float MaxScale = 1f;

        /// <summary>押した瞬間に一瞬だけ膨らむ倍率と、その長さ（秒）。</summary>
        public const float PressFlashScale = 1.6f;

        public const float PressFlashSeconds = 0.12f;

        /// <summary>点の色（theme.uss の琥珀 `#F59E0B`。板の強調色と同じ1色）。</summary>
        public static readonly Color DotColor = new Color(245f / 255f, 158f / 255f, 11f / 255f, 1f);

        /// <summary>一度に見る板の数。台所の板は5枚なので余裕を持って8。</summary>
        private const int MaxOverlaps = 8;

        [SerializeField] private XRPokeInteractor _interactor;

        private readonly Collider[] _overlaps = new Collider[MaxOverlaps];

        private Transform _dot;
        private Renderer _dotRenderer;
        private float _flashUntil;
        private bool _wasSelecting;

        /// <summary>今 点が出ているか（PlayMode 試験が見る）。</summary>
        public bool IsVisible => _dotRenderer != null && _dotRenderer.enabled;

        /// <summary>最後に測った板までの距離（m）。板が無ければ <see cref="float.PositiveInfinity"/>。</summary>
        public float LastDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>
        /// 今の点の倍率（<see cref="MinScale"/>〜<see cref="MaxScale"/>、押した瞬間だけ
        /// <see cref="PressFlashScale"/>）。奥行きの合図そのものなので PlayMode 試験が見る。
        /// </summary>
        public float CurrentScale { get; private set; }

        /// <summary>試験・シーンの組み立てから挿す。</summary>
        public void BindInteractor(XRPokeInteractor interactor) => _interactor = interactor;

        private void Awake()
        {
            if (_interactor == null)
            {
                _interactor = GetComponentInParent<XRPokeInteractor>();
            }

            CreateDot();
        }

        /// <summary>
        /// 点そのものを実行時に作る（プレハブも材質の資産も増やさない）。
        ///
        /// コライダーは必ず消す——付いたままだと、下で撫でる球が自分の点を拾って
        /// 「板が 0mm の距離にある」と誤り、さらにポークの当たり判定にも混ざる。
        /// 材質は Unlit（＝光に当たらず、そのままの色で光って見える）。URP が無ければ
        /// 素の Unlit へ落ちる。
        /// </summary>
        private void CreateDot()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Fingertip Dot";
            go.transform.SetParent(transform, false);

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            _dotRenderer = go.GetComponent<Renderer>();
            if (_dotRenderer != null)
            {
                _dotRenderer.material = CreateDotMaterial();
                _dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _dotRenderer.receiveShadows = false;
                _dotRenderer.enabled = false;
            }

            _dot = go.transform;
            _dot.localScale = Vector3.one * DotDiameterMeters;
        }

        private static Material CreateDotMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");

            var material = new Material(shader);
            material.color = DotColor;

            // URP の Unlit は _BaseColor を見る（color は _Color を書くので両方入れる）。
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", DotColor);
            }

            return material;
        }

        private void OnDestroy()
        {
            if (_dotRenderer != null && _dotRenderer.material != null)
            {
                Destroy(_dotRenderer.material);
            }
        }

        private void LateUpdate()
        {
            // LateUpdate で動かすのは、XRI が指先の姿勢を Update で更新するため
            // （Update で読むと 1 フレーム遅れて点が指から離れて見える）。
            Refresh();
        }

        /// <summary>1フレーム分の世話（試験がそのまま呼べるように分けてある）。</summary>
        public void Refresh()
        {
            if (_dot == null || _dotRenderer == null)
            {
                return;
            }

            if (_interactor == null || !_interactor.isActiveAndEnabled)
            {
                _dotRenderer.enabled = false;
                LastDistance = float.PositiveInfinity;
                return;
            }

            var tip = _interactor.GetAttachTransform(null);
            if (tip == null)
            {
                _dotRenderer.enabled = false;
                return;
            }

            var point = tip.position;
            LastDistance = NearestPanelDistance(point);

            // 触ってしまっている（板の箱の中に指がある）ときも出す。
            var near = LastDistance <= FarDistanceMeters || _interactor.hasHover;
            _dotRenderer.enabled = near;
            if (!near)
            {
                return;
            }

            // 押し込んだ瞬間（＝ XRPokeInteractor が板を掴んだ瞬間）に一瞬強く光らせる。
            var selecting = _interactor.hasSelection;
            if (selecting && !_wasSelecting)
            {
                _flashUntil = Time.unscaledTime + PressFlashSeconds;
            }

            _wasSelecting = selecting;

            _dot.position = point;
            _dot.rotation = Quaternion.identity;

            var reach = Mathf.Clamp01(
                float.IsPositiveInfinity(LastDistance) ? 1f : LastDistance / FarDistanceMeters);
            var scale = Mathf.Lerp(MaxScale, MinScale, reach);
            if (Time.unscaledTime < _flashUntil)
            {
                scale = PressFlashScale;
            }

            CurrentScale = scale;

            // 親の縮尺（rig の Camera Offset 等）に引きずられないよう打ち消す。
            var lossy = transform.lossyScale;
            var parentScale = Mathf.Max(0.0001f, Mathf.Abs(lossy.x));
            _dot.localScale = Vector3.one * (DotDiameterMeters * scale / parentScale);
        }

        /// <summary>
        /// 指先から一番近い板の面までの距離（m）。板が無ければ <see cref="float.PositiveInfinity"/>。
        ///
        /// <c>Collider.ClosestPoint</c> は点が箱の中にあれば点そのものを返すので、
        /// 板の面より奥へ入ったときは距離 0（＝点が一番大きい）になる。
        /// 板の当たり判定は裏へ 24cm 伸びている（<see cref="WorldSpacePanelFactory.PanelColliderDepth"/>）が、
        /// そこに指がある状態は「触っている」で正しい。
        /// </summary>
        private float NearestPanelDistance(Vector3 point)
        {
            var count = Physics.OverlapSphereNonAlloc(
                point, FarDistanceMeters, _overlaps, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            var nearest = float.PositiveInfinity;

            for (var i = 0; i < count; i++)
            {
                var collider = _overlaps[i];
                if (collider == null)
                {
                    continue;
                }

                // 板（＝ XRI の相手）だけを見る。台所の壁や鍋は距離計に混ぜない。
                if (collider.GetComponentInParent<XRBaseInteractable>() == null)
                {
                    continue;
                }

                var distance = Vector3.Distance(point, collider.ClosestPoint(point));
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            return nearest;
        }
    }
}
