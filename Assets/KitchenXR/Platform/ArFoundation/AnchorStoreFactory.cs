using KitchenXR.Platform.Null;
using UnityEngine;

namespace KitchenXR.Platform.ArFoundation
{
    /// <summary>
    /// 「AR Foundation が使えるなら <see cref="ArAnchorStore"/>、そうでなければ
    /// <see cref="InMemoryAnchorStore"/>」の分かれ道（設計 §4.2「どのアダプタを挿すかは1か所」）。
    ///
    /// <c>Bootstrap</c> は AR Foundation の型を知らないまま、この1行だけを呼ぶ
    /// （検算: <c>PlatformIsolationTests</c> が <c>UnityEngine.XR.ARFoundation</c> への言及を
    ///  <c>Platform/ArFoundation/</c> の外で禁じている）。
    ///
    /// 見るのは「シーンに <c>ARAnchorManager</c> が在るか」だけ。保存に対応しているかは
    /// 起動直後には分からない——AR Session が立ち上がるまでサブシステムの descriptor が無い——ので、
    /// そこは <see cref="ArAnchorStore"/> が呼ばれるたびに見て、駄目なら false／null を返す
    /// （呼び出し側が控え `panels.json` へ落ちる）。
    /// Editor の XR Simulation はマネージャこそ在るが保存に非対応なので、必ず控えの側を通る。
    /// </summary>
    public static class AnchorStoreFactory
    {
        public static IAnchorStore Create()
        {
            if (!ArAnchorStore.HasAnchorManagerInScene())
            {
                Debug.Log("[KitchenXR] ARAnchorManager が無いので、アンカーは使わず控えだけで覚えます。");
                return new InMemoryAnchorStore();
            }

            return new ArAnchorStore();
        }
    }
}
