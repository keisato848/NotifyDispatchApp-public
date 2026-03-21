namespace NotifyDispatchApp.Models;

/// <summary>
/// API 呼出結果を表すレコードです。
/// データとエラー情報の両方を保持し、呼出元でエラーを適切に処理できます。
/// </summary>
/// <typeparam name="T">結果データの型です。</typeparam>
public record FetchResult<T>
{
    /// <summary>
    /// 取得されたデータです。
    /// </summary>
    public required T Data { get; init; }

    /// <summary>
    /// API 呼出が成功したかどうかを示します。
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// エラーメッセージです。成功時は空文字です。
    /// </summary>
    public string ErrorMessage { get; init; } = "";

    /// <summary>
    /// エラー種別です。
    /// </summary>
    public FetchErrorKind ErrorKind { get; init; } = FetchErrorKind.None;

    /// <summary>
    /// リクエスト先の URL です（デバッグ用）。
    /// </summary>
    public string RequestUrl { get; init; } = "";

    /// <summary>
    /// 成功結果を生成します。
    /// </summary>
    /// <param name="data">取得データです。</param>
    /// <param name="requestUrl">リクエスト先の URL です。</param>
    /// <returns>成功を表す FetchResult です。</returns>
    public static FetchResult<T> Success(T data, string requestUrl = "") => new()
    {
        Data = data,
        IsSuccess = true,
        RequestUrl = requestUrl,
    };

    /// <summary>
    /// 失敗結果を生成します。
    /// </summary>
    /// <param name="defaultData">フォールバック用のデフォルトデータです。</param>
    /// <param name="message">エラーメッセージです。</param>
    /// <param name="kind">エラー種別です。</param>
    /// <param name="requestUrl">リクエスト先の URL です。</param>
    /// <returns>失敗を表す FetchResult です。</returns>
    public static FetchResult<T> Failure(T defaultData, string message, FetchErrorKind kind, string requestUrl = "") => new()
    {
        Data = defaultData,
        IsSuccess = false,
        ErrorMessage = message,
        ErrorKind = kind,
        RequestUrl = requestUrl,
    };
}

/// <summary>
/// API 呼出エラーの種別です。
/// </summary>
public enum FetchErrorKind
{
    /// <summary>
    /// エラーなしです。
    /// </summary>
    None,

    /// <summary>
    /// タイムアウトです。
    /// </summary>
    Timeout,

    /// <summary>
    /// ネットワークエラーです。
    /// </summary>
    Network,

    /// <summary>
    /// 認証エラーです（401/403）。
    /// </summary>
    Auth,

    /// <summary>
    /// サーバーエラーです（5xx）。
    /// </summary>
    Server,

    /// <summary>
    /// レスポンス解析エラーです。
    /// </summary>
    Parse,

    /// <summary>
    /// 不明なエラーです。
    /// </summary>
    Unknown,

    /// <summary>
    /// 一部のデータのみ取得できた状態です（中断あり）。
    /// </summary>
    Partial,
}
