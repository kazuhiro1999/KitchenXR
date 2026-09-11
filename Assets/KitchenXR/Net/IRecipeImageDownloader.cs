using System.Threading;
using Cysharp.Threading.Tasks;

namespace KitchenXR.Net
{
    /// <summary>
    /// 画像1枚を取ってくる口。<see cref="RecipeStore"/> がネットに触れる唯一の入口で、
    /// 試験ではこれを差し替える（EditMode 試験はネットに出ない）。
    /// </summary>
    public interface IRecipeImageDownloader
    {
        /// <summary>取れたらそのままの中身（PNG/JPEG のバイト列）、取れなければ null。例外は投げない。</summary>
        UniTask<byte[]> GetBytesAsync(string url, CancellationToken token);
    }
}
