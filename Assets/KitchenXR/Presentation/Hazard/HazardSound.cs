using UnityEngine;

namespace KitchenXR.Presentation.Hazard
{
    /// <summary>
    /// 領域に手が 20cm まで近づいたときの音。<see cref="PressSound"/>（釦の高い音）とは
    /// 別のクリップ（<c>Resources/Audio/hazard.wav</c>。165Hz と 110Hz の低い音 0.22 秒）で、
    /// 「押せた」の返事と「危ない」を耳で区別できるようにする。
    ///
    /// 鳴らすのは段が上がった1回だけで、さらに <see cref="HazardAlertState.SoundCooldownSeconds"/>
    /// の間は次を鳴らさない——コンロの前で作業している間ずっと鳴っては、音が意味を失う。
    /// </summary>
    public sealed class HazardSound : MonoBehaviour
    {
        public const string ClipResourcePath = "Audio/hazard";

        /// <summary>音量（0〜1）。釦の音より少し大きく、驚かせない程度。</summary>
        public const float Volume = 0.7f;

        private AudioSource _source;
        private AudioClip _clip;
        private float _nextAllowed;

        /// <summary>鳴らした回数（試験用）。クリップが無くても数える。</summary>
        public int PlayedCount { get; private set; }

        public bool HasClip => _clip != null;

        private void Awake()
        {
            _clip = Resources.Load<AudioClip>(ClipResourcePath);
            if (_clip == null)
            {
                Debug.LogWarning($"[KitchenXR] 注意の音が見つかりません: Resources/{ClipResourcePath}");
            }

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f; // 2D（頭の中で鳴る。どの領域かは線の色で分かる）。
            _source.volume = Volume;
        }

        /// <summary>鳴らす（間隔の下限に満たなければ黙る）。鳴らしたら true。</summary>
        public bool Play()
        {
            if (Time.unscaledTime < _nextAllowed)
            {
                return false;
            }

            _nextAllowed = Time.unscaledTime + HazardAlertState.SoundCooldownSeconds;
            PlayedCount++;

            if (_clip != null && _source != null)
            {
                _source.PlayOneShot(_clip);
            }

            return true;
        }
    }
}
