using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using KitchenXR.Domain;
using UnityEngine;

namespace KitchenXR.Net
{
    /// <summary>
    /// レシピ（JSON と画像）のローカル保管庫（設計 §11 追補「オフライン前提」）。
    ///
    /// 主人は調理中に電子レンジを使う——そのとき Wi-Fi は切れる。だから
    /// **表示は常に <c>Application.persistentDataPath/recipes/&lt;id&gt;/</c> から読む**。
    /// 手元に無いものだけ、そのとき取りに行って保存する。取れなければ null を返し、
    /// 呼び出し側（<see cref="KitchenXR.Presentation.RecipePanel"/>）が淡い札で代える。
    ///
    /// 見本（Resources の chahan.json）も同じ経路を通す:
    /// 初回だけ Resources の中身をそのまま persistentDataPath へ写し、以後はローカルの JSON を読む。
    /// こうしておくと P3 でサーバから取ってくるようになっても、表示側の経路は1本のまま変わらない。
    ///
    /// ネットに触れるのは <see cref="IRecipeImageDownloader"/> 越しだけ（試験では差し替える）。
    /// Domain には触れない——画像とファイルは Presentation/Net の話（設計 §4.2）。
    /// </summary>
    public sealed class RecipeStore
    {
        public const string RecipeFileName = "recipe.json";
        public const string HeroImageKey = "hero";

        /// <summary>レシピ一覧の写し（P3）。取れたら保存し、取れないときはこれを出す。</summary>
        public const string IndexFileName = "index.json";

        private readonly string _rootDirectory;
        private readonly IRecipeImageDownloader _downloader;

        /// <summary>この起動で取れなかった URL。同じものを何度も叩きに行かない（設計 §11「二重取得の抑止」）。</summary>
        private readonly HashSet<string> _failedUrls = new HashSet<string>();

        /// <summary>取得中のもの。同じ画像を2箇所から頼まれても通信は1回で済ませる。</summary>
        private readonly Dictionary<string, UniTask<bool>> _inFlight = new Dictionary<string, UniTask<bool>>();

        public RecipeStore(string rootDirectory, IRecipeImageDownloader downloader)
        {
            _rootDirectory = rootDirectory;
            _downloader = downloader;
        }

        /// <summary>実機・Editor で使う既定の保管庫（persistentDataPath 配下）。</summary>
        public static RecipeStore CreateDefault() =>
            new RecipeStore(
                Path.Combine(Application.persistentDataPath, "recipes"),
                new UnityWebRequestImageDownloader());

        public string RootDirectory => _rootDirectory;

        public static string StepImageKey(int stepIndex) => $"step_{stepIndex:00}";

        public string DirectoryFor(string recipeId) => Path.Combine(_rootDirectory, Sanitize(recipeId));

        public string RecipeJsonPath(string recipeId) => Path.Combine(DirectoryFor(recipeId), RecipeFileName);

        /// <summary>画像の置き場。中身は取得したそのまま（PNG でも JPEG でも <c>Texture2D.LoadImage</c> が見分ける）。</summary>
        public string ImagePath(string recipeId, string imageKey) =>
            Path.Combine(DirectoryFor(recipeId), Sanitize(imageKey) + ".img");

        public bool HasLocalImage(string recipeId, string imageKey) => File.Exists(ImagePath(recipeId, imageKey));

        public bool HasLocalRecipe(string recipeId) => File.Exists(RecipeJsonPath(recipeId));

        // ------------------------------------------------------------------ 一覧の写し（P3）

        /// <summary>一覧の写しの置き場（<c>&lt;root&gt;/index.json</c>）。</summary>
        public string IndexPath => Path.Combine(_rootDirectory, IndexFileName);

        public bool HasLocalIndex => File.Exists(IndexPath);

        /// <summary>manor から取れた一覧を**そのまま**写す（解釈はしない。契約が動いても写しは残る）。</summary>
        public void SaveIndexJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_rootDirectory);
                File.WriteAllText(IndexPath, json, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] レシピ一覧を控えられませんでした: {IndexPath} ({e.Message})");
            }
        }

        /// <summary>控えてある一覧。無ければ null（板は見本だけを並べる）。</summary>
        public string LoadIndexJson()
        {
            if (!File.Exists(IndexPath))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(IndexPath, Encoding.UTF8);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 控えた一覧を読めませんでした: {IndexPath} ({e.Message})");
                return null;
            }
        }

        // ------------------------------------------------------------------ レシピ本体

        /// <summary>
        /// ローカルのレシピを読む。無ければ <paramref name="bundledJson"/>（見本＝Resources の中身）を
        /// 先に書き写してから読む。読むのは**必ずローカルのファイル**。
        /// </summary>
        public UniTask<Recipe> LoadRecipeAsync(string recipeId, string bundledJson) =>
            UniTask.FromResult(LoadRecipe(recipeId, bundledJson));

        /// <summary>同期版（<see cref="LoadRecipeAsync"/> の中身。ファイルは小さいので待たせない）。</summary>
        public Recipe LoadRecipe(string recipeId, string bundledJson)
        {
            var path = RecipeJsonPath(recipeId);

            if (!File.Exists(path) && !string.IsNullOrWhiteSpace(bundledJson))
            {
                SaveRecipeJson(recipeId, bundledJson);
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"[KitchenXR] レシピが手元にありません: {path}", path);
            }

            return RecipeJson.Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>取得した契約 JSON をそのまま保存する（P3 でサーバから取ってきたときもここを通す）。</summary>
        public void SaveRecipeJson(string recipeId, string json)
        {
            Directory.CreateDirectory(DirectoryFor(recipeId));
            File.WriteAllText(RecipeJsonPath(recipeId), json, Encoding.UTF8);
        }

        // ------------------------------------------------------------------ 画像

        /// <summary>
        /// 画像を1枚返す。手元に無ければ取りに行って保存する。
        /// URL が無い工程・取れなかった工程は <see langword="null"/>（呼び出し側が札で代える）。
        /// </summary>
        public async UniTask<Texture2D> LoadImageAsync(
            string recipeId, string imageKey, string url, CancellationToken token = default)
        {
            var available = await EnsureLocalImageAsync(recipeId, imageKey, url, token);
            return available ? ReadTexture(ImagePath(recipeId, imageKey)) : null;
        }

        /// <summary>
        /// 手元に画像を揃える（テクスチャは作らない）。起動時の先読み用。
        /// 戻り値は「ローカルに在る」かどうか。
        /// </summary>
        public async UniTask<bool> EnsureLocalImageAsync(
            string recipeId, string imageKey, string url, CancellationToken token = default)
        {
            var path = ImagePath(recipeId, imageKey);
            if (File.Exists(path))
            {
                return true; // 既に手元にある＝通信しない。
            }

            if (string.IsNullOrEmpty(url) || _downloader == null || _failedUrls.Contains(url))
            {
                return false;
            }

            if (_inFlight.TryGetValue(path, out var running))
            {
                return await running; // 同じ画像を2箇所から頼まれた。通信は1回。
            }

            var task = DownloadAndSaveAsync(path, url, token).Preserve();
            _inFlight[path] = task;
            try
            {
                return await task;
            }
            finally
            {
                _inFlight.Remove(path);
            }
        }

        /// <summary>
        /// レシピの画像（hero と全工程）を手元に揃える。通信のあるうちに済ませておくためのもの。
        /// 取れなかったものは黙って飛ばす（次の起動でまた試みる）。
        /// </summary>
        public UniTask PrefetchAsync(Recipe recipe, CancellationToken token = default) =>
            PrepareAsync(recipe, null, token);

        /// <summary>
        /// 調理を始める前に画像（hero と全工程）を**全部**手元へ揃える（P3。主人の指示）。
        ///
        /// <see cref="PrefetchAsync"/> との違いは
        /// <paramref name="onProgress"/>（終わった枚数・全体の枚数）を刻むことだけ。
        /// 一覧の板が「準備中 n/m」を出すために使う——調理を始めてから
        /// 電子レンジで通信が切れても、工程の写真が出ないということが起きないようにする。
        ///
        /// 取れなかった画像は黙って飛ばす（その工程は材料名の淡い札になる）。
        /// **取れないことで調理を始められない、にはしない。**
        /// </summary>
        public async UniTask PrepareAsync(
            Recipe recipe, System.Action<int, int> onProgress, CancellationToken token = default)
        {
            if (recipe == null)
            {
                return;
            }

            var total = 1 + recipe.Steps.Count; // hero ＋ 工程の数。
            var done = 0;
            onProgress?.Invoke(done, total);

            await EnsureLocalImageAsync(recipe.Id, HeroImageKey, recipe.HeroImage, token);
            onProgress?.Invoke(++done, total);

            foreach (var step in recipe.Steps)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                await EnsureLocalImageAsync(recipe.Id, StepImageKey(step.Index), step.Image, token);
                onProgress?.Invoke(++done, total);
            }
        }

        private async UniTask<bool> DownloadAndSaveAsync(string path, string url, CancellationToken token)
        {
            var bytes = await _downloader.GetBytesAsync(url, token);
            if (bytes == null || bytes.Length == 0)
            {
                _failedUrls.Add(url); // この起動ではもう叩かない。
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return true;
        }

        private static Texture2D ReadTexture(string path)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[KitchenXR] 保存済みの画像を読めませんでした: {path} ({e.Message})");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                // 壊れたものを抱え続けない（次の起動で取り直す）。
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(texture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // 消せなくても表示は札で代わるので続ける。
                }

                return null;
            }

            return texture;
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "unknown";
            }

            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }

            return sb.ToString();
        }
    }
}
