using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KitchenXR.Platform.Null
{
    /// <summary>
    /// アンカー非対応機（Editor・シミュレータ）向けの受け皿。プロセス内メモリに置くだけで、
    /// アプリを落とすと消える。P2 で <c>ArFoundation/ArAnchorStore</c> に差し替わるまでの土台
    /// （設計 §4.3 の「対応機が無いとき」の退避路）。
    /// </summary>
    public sealed class InMemoryAnchorStore : IAnchorStore
    {
        private readonly Dictionary<string, Pose> _poses = new Dictionary<string, Pose>();

        public UniTask<bool> SaveAsync(string key, Pose pose)
        {
            _poses[key] = pose;
            return UniTask.FromResult(true);
        }

        public UniTask<Pose?> LoadAsync(string key)
        {
            return UniTask.FromResult(_poses.TryGetValue(key, out var pose) ? (Pose?)pose : null);
        }

        public UniTask ClearAsync()
        {
            _poses.Clear();
            return UniTask.CompletedTask;
        }
    }
}
