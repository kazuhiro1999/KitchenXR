using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 領域1つの見せ方——**床に投影した縁の線**を基本に、上面の枠を細く重ねる。
    /// 立体の枠は視界を塞ぐので作らない（調査 §4.3 の判断）。
    ///
    /// 線は <see cref="LineRenderer"/> 2本（床と上面）を実行時に作る。材質は Unlit で
    /// 影を落とさない——台所の床に落ちる影は「物がある」に見えてしまう。
    /// 段（<see cref="HazardAlertLevel"/>）で色と濃さだけを変え、線の形は変えない。
    ///
    /// 描いている途中だけ**上面に半透明の面**（<see cref="SetFillShown"/>）と
    /// **始点の点**（<see cref="ShowPoint"/>）を足す。線だけだと、真横から見たときや
    /// 細長い矩形のときに何も無いのと区別が付かなかった。確定した領域には出さない
    /// （台所の上に薄い板が何枚も浮くことになる）。
    /// </summary>
    public sealed class HazardZoneVisual : MonoBehaviour
    {
        /// <summary>始点の点の大きさ（直径 m）。</summary>
        public const float PointDiameterMeters = 0.03f;

        /// <summary>半透明の面の濃さ（縁の線に対する比）。</summary>
        public const float FillAlphaScale = 0.25f;

        /// <summary>床の線の太さ（m）。1cm——床のタイル目より太く、踏んでも隠れない。</summary>
        public const float FloorLineWidthMeters = 0.01f;

        /// <summary>上面の枠の太さ（m）。床より細くして主従を付ける。</summary>
        public const float TopLineWidthMeters = 0.005f;

        /// <summary>琥珀（theme.uss の <c>--color-accent</c> = #F59E0B）。</summary>
        public static readonly Color Amber = new Color(245f / 255f, 158f / 255f, 11f / 255f);

        /// <summary>赤（theme.uss の <c>--color-alarm</c> = #DC2626）。</summary>
        public static readonly Color Alarm = new Color(220f / 255f, 38f / 255f, 38f / 255f);

        /// <summary>配置モードで常時出しておくときの濃さ（領域の在り処が分かるだけの薄さ）。</summary>
        public const float FaintAlpha = 0.25f;

        /// <summary>60cm・40cm の段（軽い注意）の濃さ。</summary>
        public const float WatchAlpha = 0.7f;

        /// <summary>20cm の段（赤）の濃さ。</summary>
        public const float NearAlpha = 1f;

        private readonly Vector3[] _corners = new Vector3[4];
        private readonly Vector3[] _fillVertices = new Vector3[4];

        private LineRenderer _floor;
        private LineRenderer _top;
        private MeshRenderer _fill;
        private Mesh _fillMesh;
        private Transform _point;
        private Renderer _pointRenderer;
        private HazardZone _zone;
        private bool _fillWanted;

        /// <summary>今出ている段（試験が見る）。出ていなければ <see cref="HazardAlertLevel.Off"/>。</summary>
        public HazardAlertLevel Level { get; private set; } = HazardAlertLevel.Off;

        /// <summary>線が見えているか（試験が見る）。</summary>
        public bool IsVisible => _floor != null && _floor.enabled;

        /// <summary>半透明の面が出ているか（試験が見る）。</summary>
        public bool IsFillVisible => _fill != null && _fill.enabled;

        /// <summary>始点の点が出ているか（試験が見る）。</summary>
        public bool IsPointVisible => _pointRenderer != null && _pointRenderer.enabled;

        public HazardZone Zone => _zone;

        private void Awake()
        {
            _floor = CreateLine("Zone Floor Line", FloorLineWidthMeters);
            _top = CreateLine("Zone Top Frame", TopLineWidthMeters);
            CreateFill();
            CreatePoint();
            SetShown(false);
        }

        private void OnDestroy()
        {
            DestroyMaterial(_floor);
            DestroyMaterial(_top);

            if (_fill != null && _fill.material != null)
            {
                Destroy(_fill.material);
            }

            if (_pointRenderer != null && _pointRenderer.material != null)
            {
                Destroy(_pointRenderer.material);
            }

            if (_fillMesh != null)
            {
                Destroy(_fillMesh);
            }
        }

        private static void DestroyMaterial(LineRenderer line)
        {
            if (line != null && line.material != null)
            {
                Destroy(line.material);
            }
        }

        /// <summary>どの領域を描くか。矩形を作り直したときも同じ入口を通す。</summary>
        public void SetZone(HazardZone zone)
        {
            _zone = zone;
            Rebuild();
        }

        /// <summary>上面の半透明の面を出す／しまう（描いている途中だけ）。</summary>
        public void SetFillShown(bool shown)
        {
            _fillWanted = shown;

            if (_fill != null)
            {
                _fill.enabled = shown && _zone != null && IsVisible;
            }
        }

        /// <summary>始点の点をその場に出す（つまんだ瞬間の合図）。</summary>
        public void ShowPoint(Vector3 world)
        {
            if (_point == null)
            {
                return;
            }

            _point.position = world;
            _pointRenderer.enabled = true;
        }

        public void HidePoint()
        {
            if (_pointRenderer != null)
            {
                _pointRenderer.enabled = false;
            }
        }

        /// <summary>
        /// 段を当てる。<paramref name="alwaysShow"/> は配置モードで、離れていても
        /// 薄く出しておくため（領域が在ることが見えないと置き直せない）。
        /// </summary>
        public void SetLevel(HazardAlertLevel level, bool alwaysShow)
        {
            Level = level;

            if (_zone == null)
            {
                SetShown(false);
                return;
            }

            if (level == HazardAlertLevel.Off && !alwaysShow)
            {
                SetShown(false);
                return;
            }

            SetShown(true);

            var color = level == HazardAlertLevel.Near ? Alarm : Amber;
            var alpha = level switch
            {
                HazardAlertLevel.Near => NearAlpha,
                HazardAlertLevel.Watch => WatchAlpha,
                _ => FaintAlpha,
            };

            color.a = alpha;
            Tint(_floor, color);

            // 上面の枠は床より一段薄い（主は床の線）。
            color.a = alpha * 0.6f;
            Tint(_top, color);

            color.a = alpha * FillAlphaScale;
            TintRenderer(_fill, color);

            color.a = alpha;
            TintRenderer(_pointRenderer, color);
        }

        private void Rebuild()
        {
            if (_zone == null || _floor == null || _top == null)
            {
                return;
            }

            _zone.FloorCorners(_corners);
            SetLoop(_floor, _corners);

            _zone.TopCorners(_corners);
            SetLoop(_top, _corners);
            RebuildFill(_corners);
        }

        /// <summary>
        /// 上面の四隅から面を1枚。頂点は面の Transform のローカルへ写す——
        /// 世界の座標をそのまま入れると、根（Kitchen Hazards）がずれている場面で二重に動く。
        /// 裏からも見えるように三角形を表裏2組（矩形の下に立って見上げることがある）。
        /// </summary>
        private void RebuildFill(Vector3[] corners)
        {
            if (_fillMesh == null || _fill == null)
            {
                return;
            }

            for (var i = 0; i < 4; i++)
            {
                _fillVertices[i] = _fill.transform.InverseTransformPoint(corners[i]);
            }

            _fillMesh.Clear();
            _fillMesh.SetVertices(_fillVertices);
            _fillMesh.SetTriangles(FillTriangles, 0);
            _fillMesh.RecalculateBounds();
        }

        private static readonly int[] FillTriangles = { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };

        private static void SetLoop(LineRenderer line, Vector3[] corners)
        {
            line.positionCount = 4;
            line.loop = true;
            line.SetPositions(corners);
        }

        private static void Tint(LineRenderer line, Color color)
        {
            if (line == null)
            {
                return;
            }

            line.startColor = color;
            line.endColor = color;

            if (line.material != null)
            {
                line.material.color = color;
                if (line.material.HasProperty("_BaseColor"))
                {
                    line.material.SetColor("_BaseColor", color);
                }
            }
        }

        private static void TintRenderer(Renderer renderer, Color color)
        {
            if (renderer == null || renderer.material == null)
            {
                return;
            }

            renderer.material.color = color;
            if (renderer.material.HasProperty("_BaseColor"))
            {
                renderer.material.SetColor("_BaseColor", color);
            }
        }

        private void SetShown(bool shown)
        {
            if (_floor != null)
            {
                _floor.enabled = shown;
            }

            if (_top != null)
            {
                _top.enabled = shown;
            }

            if (_fill != null)
            {
                _fill.enabled = shown && _fillWanted;
            }
        }

        /// <summary>上面の半透明の面（描いている途中だけ出す）。</summary>
        private void CreateFill()
        {
            var go = new GameObject("Zone Top Fill");
            go.transform.SetParent(transform, false);

            _fillMesh = new Mesh { name = "Zone Top Fill" };
            go.AddComponent<MeshFilter>().sharedMesh = _fillMesh;

            _fill = go.AddComponent<MeshRenderer>();
            _fill.material = CreateLineMaterial();
            _fill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _fill.receiveShadows = false;
            _fill.enabled = false;
        }

        /// <summary>
        /// 始点の点。球にしてあるのは、どの角度から見ても同じ大きさに見えるため
        /// （面だと真横から消える）。コライダーは外す——手のピンチを奪ってしまう。
        /// </summary>
        private void CreatePoint()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Zone Start Point";

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * PointDiameterMeters;

            _point = go.transform;
            _pointRenderer = go.GetComponent<Renderer>();
            _pointRenderer.material = CreateLineMaterial();
            _pointRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _pointRenderer.receiveShadows = false;
            _pointRenderer.enabled = false;

            // 矩形がまだ無い間（点だけの段）も色が付いているように。
            TintRenderer(_pointRenderer, new Color(Amber.r, Amber.g, Amber.b, 1f));
        }

        private LineRenderer CreateLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = CreateLineMaterial();
            return line;
        }

        /// <summary>
        /// 線の材質（<see cref="FingertipCursor"/> と同じ作り）。URP が無ければ素の Unlit へ落ちる。
        /// 半透明で出したいので、見つかったシェーダに合わせて混色を立てる。
        /// </summary>
        private static Material CreateLineMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");

            var material = new Material(shader);

            // URP の Unlit は混色の状態を材質の編集画面で焼くので、実行時に作ったものは
            // 自分で立てる（_Surface だけ変えても不透明のまま描かれる）。
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f); // Transparent
                material.SetFloat("_Blend", 0f); // Alpha
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            return material;
        }
    }
}
