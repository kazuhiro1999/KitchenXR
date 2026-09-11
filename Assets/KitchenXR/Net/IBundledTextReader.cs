using System.Threading;
using Cysharp.Threading.Tasks;

namespace KitchenXR.Net
{
    /// <summary>
    /// アプリに同梱したテキスト（<c>StreamingAssets</c> の中身）を1つ読む口。
    ///
    /// Android では <c>StreamingAssets</c> は APK の中（<c>jar:file://…!/assets/…</c>）にあり、
    /// <see cref="System.IO.File"/> では読めない——<c>UnityWebRequest</c> を通す必要がある。
    /// その機種差を <see cref="MediaStore"/> から追い出すための口で、EditMode 試験では差し替える。
    /// </summary>
    public interface IBundledTextReader
    {
        /// <summary>読めたら中身、読めなければ null。例外は投げない。</summary>
        UniTask<string> ReadAsync(string path, CancellationToken token);
    }
}
