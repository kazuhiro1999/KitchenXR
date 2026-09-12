using UnityEngine;

namespace KitchenXR.Presentation
{
    /// <summary>
    /// 「同じボタンの連打を600ms抑える」の共通実装。
    /// キーごと（ボタン名や工程 index）に最後に受け付けた時刻を持つ。
    /// Domain は連打を扱わない前提なので、この抑止は必ず Presentation 側に置く。
    /// </summary>
    public sealed class ClickDebounce
    {
        private const float DebounceSeconds = 0.6f;

        private readonly System.Collections.Generic.Dictionary<string, float> _lastAcceptedTime =
            new System.Collections.Generic.Dictionary<string, float>();

        /// <summary>key の連打を 600ms 単位に間引く。受け付けたら true。</summary>
        public bool TryAccept(string key)
        {
            var now = Time.unscaledTime;
            if (_lastAcceptedTime.TryGetValue(key, out var last) && now - last < DebounceSeconds)
            {
                return false;
            }

            _lastAcceptedTime[key] = now;
            return true;
        }
    }
}
