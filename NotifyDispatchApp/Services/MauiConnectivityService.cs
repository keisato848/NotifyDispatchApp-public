using Microsoft.Maui.Networking;

namespace NotifyDispatchApp.Services;

/// <summary>
/// .NET MAUIの接続状態を参照する実装です。
/// </summary>
public sealed class MauiConnectivityService : IConnectivityService
{
    /// <summary>
    /// インターネット接続が利用可能かどうかを返します。
    /// </summary>
    /// <returns>インターネット接続が利用可能な場合はtrue。</returns>
    public bool HasInternetAccess()
    {
        return Connectivity.NetworkAccess == NetworkAccess.Internet;
    }
}
