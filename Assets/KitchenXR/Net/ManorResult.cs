namespace KitchenXR.Net
{
    /// <summary>
    /// manor への1回の問い合わせの結末。**例外を投げない**ための器。
    ///
    /// オフラインは異常ではなく前提（設計 §11 追補）。だから
    /// 「取れた」「繋がらなかった」「サーバが断った」の3つを呼び出し側が素直に分岐できるようにする。
    /// </summary>
    public sealed class ManorResult<T>
    {
        public bool IsSuccess { get; }

        /// <summary>繋がらなかった（機内モード・電子レンジ・manor が寝ている）。</summary>
        public bool IsOffline { get; }

        /// <summary>サーバが答えたときの HTTP 状態。繋がらなかったときは 0。</summary>
        public int StatusCode { get; }

        public T Value { get; }

        /// <summary>札に出す短い理由（空のこともある）。</summary>
        public string Message { get; }

        private ManorResult(bool isSuccess, bool isOffline, int statusCode, T value, string message)
        {
            IsSuccess = isSuccess;
            IsOffline = isOffline;
            StatusCode = statusCode;
            Value = value;
            Message = message ?? string.Empty;
        }

        public static ManorResult<T> Ok(T value, int statusCode = 200) =>
            new ManorResult<T>(true, false, statusCode, value, null);

        public static ManorResult<T> Offline() =>
            new ManorResult<T>(false, true, 0, default, "manor に繋がりません");

        public static ManorResult<T> Failed(int statusCode, string message) =>
            new ManorResult<T>(false, false, statusCode, default, message);
    }

    /// <summary>調理セッションの参照（manor の <c>cook-sessions</c> が返すもの）。</summary>
    public sealed class CookSessionRef
    {
        /// <summary>セッション id。未終了のものが無ければ null。</summary>
        public int? Id { get; }

        /// <summary>そのセッションのレシピ id（<c>/current</c> のときだけ返る）。</summary>
        public string RecipeId { get; }

        /// <summary>1始まりの工程番号。</summary>
        public int Current { get; }

        public CookSessionRef(int? id, string recipeId, int current)
        {
            Id = id;
            RecipeId = recipeId ?? string.Empty;
            Current = current;
        }

        /// <summary>未終了のセッションが在るか。</summary>
        public bool Exists => Id.HasValue;
    }

    /// <summary>工程イベントの返り（<c>{current, progress}</c>）。</summary>
    public sealed class CookProgress
    {
        public int Current { get; }
        public double Progress { get; }

        public CookProgress(int current, double progress)
        {
            Current = current;
            Progress = progress;
        }
    }
}
