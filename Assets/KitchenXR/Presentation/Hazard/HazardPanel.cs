using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 空間に置く注意の板1枚（火気注意・熱い・刃物…）。
    ///
    /// **調理中は触れない板**——コライダーとポークの受け口を降ろす（表示だけ）。レイの層から
    /// 外すのは <see cref="CookingModeInputGate"/> の仕事で、こちらは指の側を止める。
    /// 配置モードの間だけコライダーを戻して掴めるようにし、右横に「消す」の小さな板を出す。
    ///
    /// 「消す」の板は子ではなく**兄弟**で、この板の横に付いて回るのがここの仕事。
    /// 子にすると <see cref="UIDocument"/> が自分の板を作らず親の中の要素になってしまう。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HazardPanel : MonoBehaviour
    {
        /// <summary>この板を消してほしい（2度押しが通った）。受けるのは <see cref="HazardBoards"/>。</summary>
        public event Action<HazardPanel> DeleteRequested;

        /// <summary>アンカーと控えの鍵（<c>panel.hazard.&lt;uuid&gt;</c>）。</summary>
        public string Key { get; private set; } = string.Empty;

        /// <summary>どのプリセットから作った板か。</summary>
        public string PresetId { get; private set; } = string.Empty;

        private HazardPreset _preset;
        private HazardDeleteChip _chip;
        private bool _painted;
        private bool _placing;

        public void Bind(string key, HazardPreset preset, HazardDeleteChip chip)
        {
            Key = key ?? string.Empty;
            _preset = preset;
            PresetId = preset != null ? preset.Id : string.Empty;

            if (_chip != null)
            {
                _chip.Confirmed -= RaiseDelete;
            }

            _chip = chip;
            if (_chip != null)
            {
                _chip.Confirmed += RaiseDelete;
            }

            _painted = false;
            Paint();
        }

        /// <summary>「消す」の小さな板（配置モードの間だけ出る。試験が見る）。</summary>
        public HazardDeleteChip Chip => _chip;

        private void OnDestroy()
        {
            if (_chip == null)
            {
                return;
            }

            _chip.Confirmed -= RaiseDelete;

            // 兄弟なので一緒には消えない。板と道連れにする。
            Destroy(_chip.gameObject);
        }

        private void RaiseDelete() => DeleteRequested?.Invoke(this);

        private void Update()
        {
            // rootVisualElement が出来るのは板を組んだ frame とは限らないので、塗れるまで待つ。
            if (_painted)
            {
                return;
            }

            Paint();

            // 配置モードの最中に作った板は、枠を立てた時点で板の中身がまだ無い
            // （root が出来ていない）ことがある。塗れたところで枠を立て直す——
            // さもないと作った直後の板だけ琥珀の枠が出ない。
            if (_painted && _placing)
            {
                PanelPlacement.SetFrameVisible(gameObject, true);
            }
        }

        private void Paint()
        {
            if (_preset == null)
            {
                return;
            }

            var document = GetComponent<UIDocument>();
            var root = document != null ? document.rootVisualElement : null;
            var title = root?.Q<Label>("titleLabel");
            if (title == null)
            {
                return;
            }

            title.text = _preset.Title;

            var mark = root.Q<Label>("markLabel");
            if (mark != null)
            {
                mark.text = _preset.Mark;
            }

            var body = root.Q<Label>("bodyLabel");
            if (body != null)
            {
                body.text = _preset.Body;
            }

            var band = root.Q<VisualElement>("hazardBand");
            if (band != null)
            {
                band.EnableInClassList("hazard-band--red", _preset.Accent == HazardAccent.Red);
            }

            _painted = true;
        }

        /// <summary>
        /// 配置モードへ入った／出た。出るときはコライダーとポークの受け口を降ろす——
        /// 調理中の注意の板は見るだけで、指でもレイでも触れない。
        /// </summary>
        public void SetPlacing(bool placing)
        {
            _placing = placing;

            var collider = GetComponent<BoxCollider>();
            if (collider != null)
            {
                collider.enabled = placing;
            }

            var interactable = GetComponent<XRSimpleInteractable>();
            if (interactable != null && !placing)
            {
                // 立てるのは PanelPlacement（掴む仕掛けと同時に有効にしない）。ここは降ろすだけ。
                interactable.enabled = false;
            }

            if (_chip != null && _chip.gameObject.activeSelf != placing)
            {
                _chip.gameObject.SetActive(placing);
            }

            if (placing)
            {
                PlaceChip();
            }
        }

        /// <summary>
        /// 掴んで動かしている板に「消す」を追わせる（<c>LateUpdate</c> なのは、掴みが板を
        /// 動かし終えた後に読むため）。出ていない間は何もしない。
        /// </summary>
        private void LateUpdate()
        {
            if (_placing)
            {
                PlaceChip();
            }
        }

        /// <summary>板の右辺から <see cref="ChipGapMeters"/> 空けて、縦は板の中央に揃える。</summary>
        private void PlaceChip()
        {
            if (_chip == null)
            {
                return;
            }

            var board = GetComponent<UIDocument>();
            var chipDocument = _chip.GetComponent<UIDocument>();
            if (board == null || chipDocument == null)
            {
                return;
            }

            var width = MetersOf(board.worldSpaceSize.x, transform.localScale.x);
            var height = MetersOf(board.worldSpaceSize.y, transform.localScale.x);
            var chipWidth = MetersOf(chipDocument.worldSpaceSize.x, _chip.transform.localScale.x);

            var rotation = transform.rotation;
            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;

            // 板の原点は左上で右下へ伸びるので、中心は右へ幅の半分・下へ高さの半分。
            var boardCenter = transform.position + right * (width / 2f) - up * (height / 2f);
            var center = boardCenter + right * (width / 2f + ChipGapMeters + chipWidth / 2f);

            WristMenu.PlacePanelCentered(_chip.transform, center, rotation);
        }

        /// <summary>板と「消す」の間（m）。</summary>
        public const float ChipGapMeters = 0.02f;

        private static float MetersOf(float units, float scale) =>
            units / WorldSpacePanelFactory.PanelPixelsPerUnit * scale;
    }
}
