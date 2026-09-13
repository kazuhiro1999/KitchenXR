using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 注意の板の一式（プリセットから作る・消す・覚える・戻す）。
    ///
    /// 板そのものは既存のワールド空間 UI の板と同じ経路（<see cref="WorldSpacePanelFactory"/>）で
    /// 組み、置き場所も既存の仕組みに乗せる——<see cref="PanelPlacement"/> に鍵
    /// <c>panel.hazard.&lt;uuid&gt;</c> で登録すれば、掴んで動かす・アンカーと控えへ覚える・
    /// 起動時に アンカー → 控え → 既定 の順で戻す、が全部そのまま効く。
    /// ここが自分で持つのは「どの板が在って何の種類か」だけ（<see cref="HazardPanelFile"/>）。
    ///
    /// 作ったのが配置モードの最中でも、その場で掴めるようにするのは
    /// <see cref="PanelPlacement.Register"/> 側（配置中の登録を受け付ける）。
    /// </summary>
    public sealed class HazardBoards : MonoBehaviour
    {
        /// <summary>鍵の頭（<c>panel.hazard.&lt;uuid&gt;</c>）。</summary>
        public const string KeyPrefix = "panel.hazard.";

        /// <summary>板の大きさ（100×60 px ≒ 20×12cm）。遠目に題名が読める一番小さい板。</summary>
        public const float BoardWidthUnits = 100f;

        public const float BoardHeightUnits = 60f;

        /// <summary>「消す」の小さな板（34×24 px ≒ 6.8×4.8cm）。</summary>
        public const float ChipWidthUnits = 34f;

        public const float ChipHeightUnits = 24f;

        /// <summary>作ったとき・手元に呼び戻したときの置き場（頭の前 50cm）。</summary>
        public const float SpawnDistanceMeters = 0.5f;

        /// <summary>複数枚を横に並べる間隔（m）。板の幅 20cm ＋ 5cm。</summary>
        public const float SpawnSpacingMeters = 0.25f;

        /// <summary>目線より少し下（m）。作業中の手が通る高さに置かない。</summary>
        public const float SpawnBelowEyelineMeters = 0.1f;

        [SerializeField]
        [Tooltip("板の PanelSettings（他の板と同じ KitchenPanelSettings）。")]
        private PanelSettings _panelSettings;

        [SerializeField]
        [Tooltip("注意の板の uxml（HazardPanel.uxml）。")]
        private VisualTreeAsset _boardUxml;

        [SerializeField]
        [Tooltip("「消す」の小さな板の uxml（HazardDelete.uxml）。")]
        private VisualTreeAsset _deleteUxml;

        [SerializeField]
        [Tooltip("板を出す向きの基準。未指定なら Camera.main。")]
        private Transform _headTransform;

        private PanelPlacement _placement;
        private HazardPanelFile _file;
        private HazardPresetCatalog _catalog;

        private readonly List<HazardPanel> _panels = new List<HazardPanel>();

        /// <summary>今出ている注意の板（試験と診断用）。</summary>
        public IReadOnlyList<HazardPanel> Panels => _panels;

        /// <summary>板が増えた／減った（手元のメニューの札を書き換える契機）。</summary>
        public event Action Changed;

        private bool _placing;

        /// <summary>
        /// 組み立て。null を渡した引数はシーンで挿したものをそのまま使う
        /// （板の資産まで渡せるのは、PlayMode 試験がシーンと同じ経路を通れるようにするため）。
        /// </summary>
        public void Bind(
            PanelPlacement placement, HazardPanelFile file, HazardPresetCatalog catalog,
            Transform head = null, PanelSettings panelSettings = null,
            VisualTreeAsset boardUxml = null, VisualTreeAsset deleteUxml = null)
        {
            _placement = placement;
            _file = file;
            _catalog = catalog;

            if (head != null)
            {
                _headTransform = head;
            }

            if (panelSettings != null)
            {
                _panelSettings = panelSettings;
            }

            if (boardUxml != null)
            {
                _boardUxml = boardUxml;
            }

            if (deleteUxml != null)
            {
                _deleteUxml = deleteUxml;
            }
        }

        /// <summary>
        /// 控え（<c>hazards.json</c>）から板を立て直す。位置はここでは決めない——
        /// <see cref="PanelPlacement.RestoreAsync"/> が鍵ごとに アンカー → 控え → 既定 で戻すので、
        /// 登録だけ先に済ませておく必要がある（<c>Bootstrap</c> の Awake で呼ぶ）。
        /// </summary>
        public void Restore()
        {
            if (_file == null || _catalog == null)
            {
                return;
            }

            var restored = 0;
            foreach (var record in _file.Load())
            {
                if (!_catalog.TryGet(record.PresetId, out var preset))
                {
                    // 知らない種類（プリセットを入れ替えた）。板は作らず黙って落とす。
                    continue;
                }

                if (Create(record.Key, preset) != null)
                {
                    restored++;
                }
            }

            if (restored > 0)
            {
                Debug.Log($"[KitchenXR] 注意の板を {restored} 枚 戻しました。");
            }
        }

        /// <summary>プリセットから1枚作って頭の前へ出す（そのまま掴んで置ける）。</summary>
        public HazardPanel Add(string presetId)
        {
            if (_catalog == null || !_catalog.TryGet(presetId, out var preset))
            {
                return null;
            }

            var panel = Create(KeyPrefix + Guid.NewGuid().ToString("N"), preset);
            if (panel == null)
            {
                return null;
            }

            PlaceAtHand(panel, _panels.Count - 1);
            Save();
            Changed?.Invoke();
            return panel;
        }

        /// <summary>1枚消す（鍵ごと。控えからも落とす）。</summary>
        public bool Remove(HazardPanel panel)
        {
            if (panel == null || !_panels.Remove(panel))
            {
                return false;
            }

            _placement?.Unregister(panel.Key);
            Destroy(panel.gameObject);

            Save();
            Changed?.Invoke();
            return true;
        }

        /// <summary>控え（<c>hazards.json</c>）を書く。位置は <see cref="PanelPlacement"/> 側。</summary>
        public void Save()
        {
            if (_file == null)
            {
                return;
            }

            var records = new List<HazardPanelRecord>(_panels.Count);
            foreach (var panel in _panels)
            {
                records.Add(new HazardPanelRecord(panel.Key, panel.PresetId));
            }

            _file.Save(records);
        }

        /// <summary>
        /// 配置モードへ入った／出た。注意の板は調理中は触れない板なので、
        /// コライダーと「消す」の板の出し入れをここで揃える。
        /// </summary>
        public void SetPlacing(bool placing)
        {
            _placing = placing;
            foreach (var panel in _panels)
            {
                panel.SetPlacing(placing);
            }
        }

        /// <summary>
        /// 全部を頭の前へ並べ直す（「板を手元に」と、起動直後の既定の置き場）。
        /// 覚えている位置があれば <see cref="PanelPlacement.RestoreAsync"/> が後から上書きする。
        /// </summary>
        public void PlaceAllAtHand()
        {
            for (var i = 0; i < _panels.Count; i++)
            {
                PlaceAtHand(_panels[i], i);
            }
        }

        // ---------------------------------------------------------------- 板を組む

        private HazardPanel Create(string key, HazardPreset preset)
        {
            if (_panelSettings == null || _boardUxml == null)
            {
                Debug.LogWarning("[KitchenXR] 注意の板の資産（PanelSettings・uxml）が挿さっていません。");
                return null;
            }

            // 板は眠らせたまま組む（他の板と同じ順）。<see cref="UIDocument"/> は
            // panelSettings と visualTreeAsset を挿すたびに中身を作り直すので、
            // 生きたまま組むと同じ frame に2度作り直すことになる。
            var go = new GameObject($"HazardPanel ({preset.Id})");
            go.SetActive(false);
            go.transform.SetParent(transform, false);

            WorldSpacePanelFactory.Configure(
                go, _panelSettings, _boardUxml, BoardWidthUnits, BoardHeightUnits);

            go.SetActive(true);

            var chip = CreateChip();

            var panel = go.AddComponent<HazardPanel>();
            panel.Bind(key, preset, chip);
            panel.DeleteRequested += HandleDeleteRequested;
            panel.SetPlacing(_placing);

            _panels.Add(panel);

            // 鍵で登録すると、掴み・アンカー・控え・復元の全部が既存の仕組みに乗る。
            _placement?.Register(key, panel);

            return panel;
        }

        /// <summary>
        /// 「消す」の小さな板を1枚。注意の板の**子にはしない**のが肝心——
        /// <see cref="UIDocument"/> は親に UIDocument が居ると自分の板を作らず、親の中の要素に
        /// なってしまう（＝自分のコライダーと当たり点が噛み合わなくなる）。兄弟として置いて、
        /// 注意の板の横に付いて回るのは <see cref="HazardPanel"/> の仕事にする。
        /// </summary>
        private HazardDeleteChip CreateChip()
        {
            if (_deleteUxml == null)
            {
                return null;
            }

            var go = new GameObject("HazardDelete");
            go.SetActive(false); // 出るのは配置モードの間だけ。
            go.transform.SetParent(transform, false);

            WorldSpacePanelFactory.Configure(
                go, _panelSettings, _deleteUxml, ChipWidthUnits, ChipHeightUnits);

            return go.AddComponent<HazardDeleteChip>();
        }

        private void HandleDeleteRequested(HazardPanel panel) => Remove(panel);

        /// <summary>
        /// <paramref name="index"/> 枚目を頭の前 50cm・目線より少し下へ。
        /// 2枚目以降は右へ 25cm ずつずらして重ならないようにする。
        /// </summary>
        private void PlaceAtHand(HazardPanel panel, int index)
        {
            var head = _headTransform != null
                ? _headTransform
                : Camera.main != null ? Camera.main.transform : null;

            if (head == null || panel == null)
            {
                return;
            }

            var forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            // 板の「表」は local -Z（他の板と同じ約束）。
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            var right = rotation * Vector3.right;

            var center = head.position
                         + forward * SpawnDistanceMeters
                         + Vector3.down * SpawnBelowEyelineMeters
                         + right * (index * SpawnSpacingMeters);

            WristMenu.PlacePanelCentered(panel.transform, center, rotation);
        }
    }
}
