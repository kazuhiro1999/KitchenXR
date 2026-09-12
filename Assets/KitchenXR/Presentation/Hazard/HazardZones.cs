using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Platform;
using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// コンロ等の領域の一式（描く・直す・消す・覚える・戻す）。
    ///
    /// 保存は板と同じ二段構え——アンカー（鍵 <c>zone.&lt;id&gt;</c>）に中心の Pose を、
    /// 控え <c>zones.json</c> に XR Origin 基準の相対で全部（中心・広さ・向き・高さ）を書く。
    /// アンカーは位置と向きしか持てないので、広さは必ず控えの側から来る
    /// （＝アンカーが復元できた領域でも <c>zones.json</c> が要る）。
    ///
    /// 床の高さは XR Origin の y を使う。Quest の追跡原点は床なので、これが一番素直で、
    /// AR の平面検出（コンロも調理台もラベルが無い）に頼らずに済む。
    /// </summary>
    public sealed class HazardZones : MonoBehaviour
    {
        /// <summary>アンカーの鍵の頭。</summary>
        public const string AnchorKeyPrefix = "zone.";

        /// <summary>手元のメニューの ±5cm。</summary>
        public const float HeightStepMeters = 0.05f;

        [SerializeField]
        [Tooltip("控えの基準（XR Origin）。床の高さもここから取る。")]
        private Transform _originTransform;

        [SerializeField]
        [Tooltip("領域を描く仕掛け（同じ根に載せる）。")]
        private HazardZoneDrawing _drawing;

        private HazardZoneFile _file;
        private IAnchorStore _anchors;

        private readonly List<ZoneEntry> _entries = new List<ZoneEntry>();

        private HazardZoneVisual _preview;

        /// <summary>領域が増えた／減った／高さが変わった（札の書き換えの契機）。</summary>
        public event Action Changed;

        /// <summary>今の領域（試験と近接の判定が見る）。</summary>
        public IReadOnlyList<ZoneEntry> Entries => _entries;

        public int Count => _entries.Count;

        /// <summary>今 領域を描いている最中か。</summary>
        public bool IsDrawing => _drawing != null && (_drawing.IsArmed || _drawing.IsDragging);

        public void Bind(
            HazardZoneFile file, IAnchorStore anchors, Transform origin = null,
            HazardZoneDrawing drawing = null)
        {
            _file = file;
            _anchors = anchors;

            if (origin != null)
            {
                _originTransform = origin;
            }

            if (drawing != null)
            {
                _drawing = drawing;
            }

            if (_drawing != null)
            {
                _drawing.Committed -= HandleCommitted;
                _drawing.Progress -= HandleProgress;
                _drawing.Committed += HandleCommitted;
                _drawing.Progress += HandleProgress;
            }
        }

        private void OnDestroy()
        {
            if (_drawing != null)
            {
                _drawing.Committed -= HandleCommitted;
                _drawing.Progress -= HandleProgress;
            }
        }

        // ---------------------------------------------------------------- 覚える・戻す

        /// <summary>覚えている領域を戻す。順は アンカー → 控え（広さは常に控えから）。</summary>
        public async UniTask RestoreAsync(CancellationToken token = default)
        {
            Clear();

            if (_file == null)
            {
                return;
            }

            foreach (var stored in _file.Load())
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                var zone = ToWorld(stored);

                if (_anchors != null)
                {
                    try
                    {
                        var anchored = await _anchors.LoadAsync(AnchorKeyPrefix + stored.Id);
                        if (anchored.HasValue)
                        {
                            // アンカーが持つのは中心と向きだけ。広さと高さは控えの側。
                            zone = new HazardZone(
                                stored.Id, stored.Kind, anchored.Value.position,
                                stored.SizeX, stored.SizeZ,
                                anchored.Value.rotation.eulerAngles.y, stored.Height);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[KitchenXR] 領域のアンカーを読めませんでした（{stored.Id}）: {e.Message}");
                    }
                }

                AddEntry(zone);
            }

            if (_entries.Count > 0)
            {
                Debug.Log($"[KitchenXR] 領域を {_entries.Count} 個 戻しました。");
            }

            Changed?.Invoke();
        }

        /// <summary>全部を覚える（アンカーと控えの両方へ。板の保存と同じ流儀）。</summary>
        public async UniTask SaveAsync(CancellationToken token = default)
        {
            var relative = new List<HazardZone>(_entries.Count);
            var anchored = 0;

            foreach (var entry in _entries)
            {
                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (_anchors != null)
                {
                    try
                    {
                        var pose = new Pose(
                            entry.Zone.Center, Quaternion.Euler(0f, entry.Zone.YawDegrees, 0f));
                        if (await _anchors.SaveAsync(AnchorKeyPrefix + entry.Zone.Id, pose))
                        {
                            anchored++;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[KitchenXR] 領域のアンカーを書けませんでした（{entry.Zone.Id}）: {e.Message}");
                    }
                }

                relative.Add(ToRelative(entry.Zone));
            }

            _file?.Save(relative);

            if (_entries.Count > 0)
            {
                Debug.Log($"[KitchenXR] 領域を覚えました（アンカー {anchored}/{_entries.Count} 個・控えは全部）。");
            }
        }

        // ---------------------------------------------------------------- 描く

        /// <summary>「囲む」——次のピンチで始点を取る。</summary>
        public void BeginDraw() => _drawing?.Arm();

        /// <summary>描くのをやめる（「戻る」）。仮の枠も消す。</summary>
        public void CancelDraw()
        {
            _drawing?.Disarm();
            HidePreview();
        }

        /// <summary>「やり直す」——最後の領域を捨てて、もう一度囲む。</summary>
        public void Redo()
        {
            RemoveLast();
            BeginDraw();
        }

        /// <summary>「消す」——最後に作った領域を消す（2度押しの先）。</summary>
        public bool RemoveLast()
        {
            if (_entries.Count == 0)
            {
                return false;
            }

            var last = _entries[_entries.Count - 1];
            _entries.RemoveAt(_entries.Count - 1);

            if (last.Visual != null)
            {
                Destroy(last.Visual.gameObject);
            }

            SaveFileOnly();
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 最後に作った領域の上面の高さを ±<paramref name="steps"/>×5cm ずらす。
        /// レイの当たり点は天板よりやや手前／奥に落ちるので、あとから合わせられるように。
        /// </summary>
        public bool AdjustHeight(int steps)
        {
            if (_entries.Count == 0 || steps == 0)
            {
                return false;
            }

            var last = _entries[_entries.Count - 1];
            last.Zone.OffsetHeight(steps * HeightStepMeters);
            last.Visual?.SetZone(last.Zone);

            SaveFileOnly();
            Changed?.Invoke();
            return true;
        }

        /// <summary>最後に作った領域（札に高さを出すため）。無ければ null。</summary>
        public HazardZone Last => _entries.Count == 0 ? null : _entries[_entries.Count - 1].Zone;

        private void HandleProgress()
        {
            if (_drawing == null || !_drawing.IsDragging)
            {
                return;
            }

            var zone = HazardZone.FromCorners(
                "preview", HazardZone.StoveKind, _drawing.Start, _drawing.Current,
                _drawing.YawDegrees, FloorY);

            if (zone == null)
            {
                // まだ小さすぎる（ドラッグを始めた直後）。仮の枠は出さない。
                HidePreview();
                return;
            }

            EnsurePreview().SetZone(zone);
            EnsurePreview().SetLevel(HazardAlertLevel.Watch, true);
        }

        private void HandleCommitted(Vector3 start, Vector3 end, float yaw)
        {
            HidePreview();

            var zone = HazardZone.FromCorners(
                Guid.NewGuid().ToString("N"), HazardZone.StoveKind, start, end, yaw, FloorY);

            if (zone == null)
            {
                Debug.Log("[KitchenXR] 囲んだ範囲が小さすぎるので領域を作りませんでした。");
                Changed?.Invoke();
                return;
            }

            AddEntry(zone);
            SaveFileOnly();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- 中身

        private void Clear()
        {
            foreach (var entry in _entries)
            {
                if (entry.Visual != null)
                {
                    Destroy(entry.Visual.gameObject);
                }
            }

            _entries.Clear();
        }

        private ZoneEntry AddEntry(HazardZone zone)
        {
            var go = new GameObject($"HazardZone ({zone.Kind})");
            go.transform.SetParent(transform, false);

            var visual = go.AddComponent<HazardZoneVisual>();
            visual.SetZone(zone);
            visual.SetLevel(HazardAlertLevel.Off, false);

            var entry = new ZoneEntry(zone, visual);
            _entries.Add(entry);
            return entry;
        }

        private HazardZoneVisual EnsurePreview()
        {
            if (_preview != null)
            {
                return _preview;
            }

            var go = new GameObject("HazardZone (仮)");
            go.transform.SetParent(transform, false);
            _preview = go.AddComponent<HazardZoneVisual>();
            return _preview;
        }

        private void HidePreview() => _preview?.SetLevel(HazardAlertLevel.Off, false);

        /// <summary>控えだけ書く（アンカーは「保存」のときだけ。描いた直後に消えないように）。</summary>
        private void SaveFileOnly()
        {
            if (_file == null)
            {
                return;
            }

            var relative = new List<HazardZone>(_entries.Count);
            foreach (var entry in _entries)
            {
                relative.Add(ToRelative(entry.Zone));
            }

            _file.Save(relative);
        }

        /// <summary>床の高さ。XR Origin が床（Quest の追跡原点）なのでその y。</summary>
        private float FloorY => _originTransform != null ? _originTransform.position.y : 0f;

        private HazardZone ToRelative(HazardZone zone)
        {
            if (_originTransform == null)
            {
                return zone;
            }

            var center = _originTransform.InverseTransformPoint(zone.Center);
            var yaw = zone.YawDegrees - _originTransform.rotation.eulerAngles.y;
            return new HazardZone(zone.Id, zone.Kind, center, zone.SizeX, zone.SizeZ, yaw, zone.Height);
        }

        private HazardZone ToWorld(HazardZone zone)
        {
            if (_originTransform == null)
            {
                return zone;
            }

            var center = _originTransform.TransformPoint(zone.Center);
            var yaw = zone.YawDegrees + _originTransform.rotation.eulerAngles.y;
            return new HazardZone(zone.Id, zone.Kind, center, zone.SizeX, zone.SizeZ, yaw, zone.Height);
        }

        /// <summary>領域1つと、その線と、近さの段。</summary>
        public sealed class ZoneEntry
        {
            public ZoneEntry(HazardZone zone, HazardZoneVisual visual)
            {
                Zone = zone;
                Visual = visual;
            }

            public HazardZone Zone { get; }
            public HazardZoneVisual Visual { get; }
            public HazardAlertState Alert { get; } = new HazardAlertState();
        }
    }
}
