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
    /// </summary>
    public sealed class HazardZoneVisual : MonoBehaviour
    {
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

        private LineRenderer _floor;
        private LineRenderer _top;
        private HazardZone _zone;

        /// <summary>今出ている段（試験が見る）。出ていなければ <see cref="HazardAlertLevel.Off"/>。</summary>
        public HazardAlertLevel Level { get; private set; } = HazardAlertLevel.Off;

        /// <summary>線が見えているか（試験が見る）。</summary>
        public bool IsVisible => _floor != null && _floor.enabled;

        public HazardZone Zone => _zone;

        private void Awake()
        {
            _floor = CreateLine("Zone Floor Line", FloorLineWidthMeters);
            _top = CreateLine("Zone Top Frame", TopLineWidthMeters);
            SetShown(false);
        }

        private void OnDestroy()
        {
            DestroyMaterial(_floor);
            DestroyMaterial(_top);
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
        }

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
