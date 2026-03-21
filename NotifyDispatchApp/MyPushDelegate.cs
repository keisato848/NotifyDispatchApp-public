
using Microsoft.Extensions.Logging;
using Shiny.Push;

namespace NotifyDispatchApp;

/// <summary>
/// プッシュ通知イベントのデリゲート実装です。
/// </summary>
public class MyPushDelegate(IPushManager _pushManager, ILogger<MyPushDelegate> logger) : IPushDelegate
{
    /// <summary>
    /// プッシュ通知がタップされた際に呼び出されます。
    /// </summary>
    /// <param name="push">通知データです。</param>
    /// <returns>完了したタスクです。</returns>
    public Task OnEntry(PushNotification push)
    {
        logger.LogInformation("プッシュ通知がタップされました。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// プッシュ通知を受信した際に呼び出されます。
    /// </summary>
    /// <param name="push">通知データです。</param>
    /// <returns>完了したタスクです。</returns>
    public Task OnReceived(PushNotification push)
    {
        logger.LogInformation("プッシュ通知を受信しました。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 新しいプッシュ通知トークンが発行された際に呼び出されます。
    /// </summary>
    /// <param name="token">発行されたトークンです。</param>
    /// <returns>完了したタスクです。</returns>
    public Task OnNewToken(string token)
    {
        logger.LogInformation("プッシュトークンが更新されました。");
        return Task.CompletedTask;
    }

    /// <summary>
    /// プッシュ通知の登録が解除された際に呼び出されます。
    /// </summary>
    /// <param name="token">解除対象のトークンです。</param>
    /// <returns>完了したタスクです。</returns>
    public Task OnUnRegistered(string token)
    {
        logger.LogInformation("プッシュ通知の登録が解除されました。");
        return Task.CompletedTask;
    }
}

