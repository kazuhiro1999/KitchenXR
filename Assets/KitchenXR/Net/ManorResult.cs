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

    /// <summary>
    /// ペアリングの始まり（<c>pair/start</c> の返り。manor の ADR-017 D2-1）。
    /// <see cref="Code"/> を板に大きく出し、主人が manor の Web の 設定 → 端末 に入れて許可する。
    /// </summary>
    public sealed class PairStart
    {
        /// <summary>番号が読めなかったときの保険（ADR-017 D2-4 の「5 分で失効」）。</summary>
        public const int DefaultExpiresIn = 300;

        /// <summary>同じく、訊く間隔の既定（ADR-017 D2-2）。</summary>
        public const int DefaultPollAfter = 2;

        /// <summary>照合用（端末はこれで訊く）。主人には見せない。</summary>
        public string PairId { get; }

        /// <summary>表示用の6桁。</summary>
        public string Code { get; }

        /// <summary>番号の寿命（秒）。切れたら新しい番号を取り直す。</summary>
        public int ExpiresInSeconds { get; }

        /// <summary>訊く間隔（秒）。manor が決める（叩きすぎないように）。</summary>
        public int PollAfterSeconds { get; }

        public PairStart(string pairId, string code, int expiresInSeconds, int pollAfterSeconds)
        {
            PairId = pairId ?? string.Empty;
            Code = code ?? string.Empty;
            ExpiresInSeconds = expiresInSeconds > 0 ? expiresInSeconds : DefaultExpiresIn;
            PollAfterSeconds = pollAfterSeconds > 0 ? pollAfterSeconds : DefaultPollAfter;
        }
    }

    /// <summary>
    /// ペアリングの進み（<c>pair/poll</c> の返り）。状態は3つだけ（ADR-017 §4.2）——
    /// 知らない <c>pair_id</c>・期限切れ・既に鍵を渡した後は、全部 <c>expired</c> で来る。
    /// </summary>
    public sealed class PairPoll
    {
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Expired = "expired";

        public string Status { get; }

        /// <summary>端末の鍵。<c>approved</c> のときだけ入る（**一度しか返らない**）。</summary>
        public string Token { get; }

        public string DeviceId { get; }

        /// <summary>この端末が振る舞う利用者（ADR-014）。</summary>
        public string UserId { get; }

        public PairPoll(string status, string token, string deviceId, string userId)
        {
            Status = status ?? string.Empty;
            Token = token ?? string.Empty;
            DeviceId = deviceId ?? string.Empty;
            UserId = userId ?? string.Empty;
        }

        /// <summary>許可された（鍵が入っている）。鍵が空なら許可とは見なさない。</summary>
        public bool IsApproved => Status == Approved && !string.IsNullOrEmpty(Token);

        public bool IsPending => Status == Pending;

        /// <summary>番号が死んだ（新しい番号を取り直す）。知らない語が来たときもこちらに寄せる。</summary>
        public bool IsExpired => !IsApproved && !IsPending;
    }
}
