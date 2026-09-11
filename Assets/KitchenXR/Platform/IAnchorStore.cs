using Cysharp.Threading.Tasks;
using UnityEngine;

namespace KitchenXR.Platform
{
    /// <summary>
    /// パネル1枚＝鍵1つでアンカーを保存・復元する口（設計 §4.3）。
    /// 「台所全体の座標系」は作らない——パネルごとに置くほうが素直、という設計判断。
    /// Pose は UnityEngine の型なので、この口は Runtime asmdef 側に置く。
    /// P0/P1 では <see cref="Null.InMemoryAnchorStore"/> しか挿さない。ArFoundation 実装は P2。
    /// </summary>
    public interface IAnchorStore
    {
        UniTask<bool> SaveAsync(string key, Pose pose);
        UniTask<Pose?> LoadAsync(string key);
        UniTask ClearAsync();
    }
}
