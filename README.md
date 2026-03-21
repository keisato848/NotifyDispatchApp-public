# とやま防災ナビ (NotifyDispatchApp)

富山県の消防出動情報・熊出没情報・安全情報を表示する .NET MAUI モバイルアプリです。

## 機能

- 消防出動情報の一覧表示・検索・フィルタリング
- 熊出没情報の地図表示（クラスタリング対応）
- 安全情報の表示
- プッシュ通知（Azure Notification Hubs）

## 必要環境

- .NET 10 SDK
- MAUI workload (`dotnet workload install maui`)
- Android SDK (Android ビルド用)
- Xcode (iOS ビルド用、macOS のみ)

## セットアップ

### 1. リポジトリをクローン

```bash
git clone https://github.com/keisato848/NotifyDispatchApp-public.git
cd NotifyDispatchApp-public
```

### 2. API キーの設定

#### appsettings.json

`NotifyDispatchApp/appsettings.json` のプレースホルダーを実際の値に置き換えてください：

```json
{
  "Api": {
    "BaseUrl": "https://<your-function-app>.azurewebsites.net",
    "FunctionKey": "<your-function-key>",
    "DispatchUrl": "https://<your-dispatch-api>.azurewebsites.net/api/Function1",
    "DispatchApiKey": "<your-dispatch-api-key>"
  }
}
```

#### appsettings.secrets.json（オプション）

機密情報を別ファイルで管理する場合は `NotifyDispatchApp/appsettings.secrets.json` を作成してください。このファイルは `.gitignore` で除外されています。

```json
{
  "Api": {
    "FunctionKey": "<your-function-key>"
  }
}
```

#### Google Maps API キー

`NotifyDispatchApp/Platforms/Android/AndroidManifest.xml` で `YOUR_GOOGLE_MAPS_API_KEY` を実際のキーに置き換えてください。

### 3. ビルド・実行

```bash
# restore
dotnet restore

# テスト実行
dotnet test NotifyDispatchApp.Tests/NotifyDispatchApp.Tests.csproj

# Android ビルド
dotnet build NotifyDispatchApp/NotifyDispatchApp.csproj -f net10.0-android

# iOS ビルド (macOS のみ)
dotnet build NotifyDispatchApp/NotifyDispatchApp.csproj -f net10.0-ios
```

## プロジェクト構成

| プロジェクト | 説明 |
|---|---|
| `NotifyDispatchApp` | メイン MAUI アプリ |
| `NotifyDispatchApp.Core` | 共有ライブラリ（Models, Services） |
| `NotifyDispatchApp.Tests` | ユニットテスト (xUnit) |
| `NotifyDispatchApp.DeviceTests` | デバイステスト |
| `NotifyDispatchApp.UITests` | UI テスト (Appium) |

## ライセンス

MIT
