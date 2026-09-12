using UnityEngine;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// ボタンを押したときの効果音。
    ///
    /// ホログラムの板には手応えが無い。XRI の hover で振動は出るが「発火した」瞬間の返事が
    /// 無いので、<see cref="PokePress.Pressed"/>（押し下げで発火した、その時）に短い音を1つ
    /// 鳴らす。板ごとに置かず、起動時に自分で1つだけ立つ（<see cref="Install"/>）——
    /// どの板から押されても同じ音で、シーンの組み立てに手を入れないため。
    ///
    /// 音は 2D（頭の中で鳴る）。板の位置から鳴らす方が上品だが、台所の板は腕の届く範囲にしか
    /// 無いので差が出ない。クリップは <c>Resources/Audio/press.wav</c>（70ms の減衰する高い音）。
    /// </summary>
    public sealed class PressSound : MonoBehaviour
    {
        public const string ClipResourcePath = "Audio/press";

        /// <summary>音量（0〜1）。調理中の環境音に負けず、驚かせない程度。</summary>
        public const float Volume = 0.6f;

        private AudioSource _source;
        private AudioClip _clip;

        /// <summary>鳴らした回数（試験用）。クリップが無くても数える。</summary>
        public int PlayedCount { get; private set; }

        public bool HasClip => _clip != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<PressSound>() != null)
            {
                return;
            }

            var go = new GameObject("PressSound");
            DontDestroyOnLoad(go);
            go.AddComponent<PressSound>();
        }

        private void Awake()
        {
            _clip = Resources.Load<AudioClip>(ClipResourcePath);
            if (_clip == null)
            {
                Debug.LogWarning($"[KitchenXR] 効果音が見つかりません: Resources/{ClipResourcePath}");
            }

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.volume = Volume;
        }

        private void OnEnable() => PokePress.Pressed += Play;

        private void OnDisable() => PokePress.Pressed -= Play;

        private void Play()
        {
            PlayedCount++;
            if (_clip != null && _source != null)
            {
                _source.PlayOneShot(_clip);
            }
        }
    }
}
