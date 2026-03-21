using System.Globalization;
using CoreGraphics;
using Foundation;
using UIKit;

namespace NotifyDispatchApp.Platforms.iOS.Handlers;

/// <summary>
/// クラスタマーカー用の丸アイコン UIImage を生成する静的クラスです。
/// 件数に応じたサイズとカテゴリ色で円形マーカーを描画します。
/// </summary>
public static class ClusterIconGenerator
{
    /// <summary>
    /// サイズ区分ごとの pt サイズとテキスト pt サイズの定義です。
    /// </summary>
    private static readonly (int MinCount, nfloat SizePt, nfloat TextPt)[] SizeCategories =
    [
        (100, 64, 20),
        (50, 56, 18),
        (10, 48, 16),
        (2, 40, 14),
    ];

    /// <summary>
    /// デフォルトのサイズ（pt）です。
    /// </summary>
    private const float DefaultSizePt = 40f;

    /// <summary>
    /// デフォルトのテキストサイズ（pt）です。
    /// </summary>
    private const float DefaultTextPt = 14f;

    /// <summary>
    /// 背景色のアルファ値です（0.85）。
    /// </summary>
    private const float BackgroundAlpha = 0.85f;

    /// <summary>
    /// 白縁取りの太さ（pt）です。
    /// </summary>
    private const float StrokeWidthPt = 2f;

    /// <summary>
    /// UIImage キャッシュです。キー: (件数, colorHex)。
    /// 同一件数・同一色の組み合わせはキャッシュから返します。
    /// </summary>
    private static readonly Dictionary<(int Count, string ColorHex), UIImage> _cache = [];

    /// <summary>
    /// 指定件数とカテゴリ色でクラスタマーカー用の UIImage を生成します。
    /// 同じ件数・色の組み合わせはキャッシュから返します。
    /// </summary>
    /// <param name="count">クラスタに含まれるアイテム数です。</param>
    /// <param name="colorHex">カテゴリのテーマカラー（例: "#E53935"）です。</param>
    /// <returns>マーカーに設定する UIImage です。</returns>
    public static UIImage Create(int count, string colorHex)
    {
        var cacheKey = (count, colorHex);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var (sizePt, textPt) = GetSizeSpec(count);
        var image = Render(count, colorHex, sizePt, textPt);
        _cache[cacheKey] = image;
        return image;
    }

    /// <summary>
    /// キャッシュ内の全 UIImage を Dispose し、キャッシュをクリアします。
    /// ClusterMapHandler.DisconnectHandler から呼び出します。
    /// </summary>
    public static void ClearCache()
    {
        foreach (var img in _cache.Values)
        {
            img.Dispose();
        }
        _cache.Clear();
    }

    /// <summary>
    /// Hex 文字列を UIColor に変換します。
    /// </summary>
    /// <param name="hex">色の Hex 文字列（例: "#E53935"）です。</param>
    /// <returns>対応する UIColor です。</returns>
    internal static UIColor ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        var r = int.Parse(hex[0..2], NumberStyles.HexNumber) / 255f;
        var g = int.Parse(hex[2..4], NumberStyles.HexNumber) / 255f;
        var b = int.Parse(hex[4..6], NumberStyles.HexNumber) / 255f;
        return new UIColor(r, g, b, 1f);
    }

    /// <summary>
    /// 件数からサイズ区分を決定します。
    /// </summary>
    /// <param name="count">アイテム数です。</param>
    /// <returns>サイズ pt とテキスト pt のタプルです。</returns>
    private static (nfloat SizePt, nfloat TextPt) GetSizeSpec(int count)
    {
        foreach (var (minCount, sizePt, textPt) in SizeCategories)
        {
            if (count >= minCount)
                return (sizePt, textPt);
        }
        return (DefaultSizePt, DefaultTextPt);
    }

    /// <summary>
    /// Core Graphics を使用してクラスタアイコンの UIImage を描画します。
    /// </summary>
    /// <param name="count">表示する件数です。</param>
    /// <param name="colorHex">背景の色コード（Hex）です。</param>
    /// <param name="sizePt">アイコンサイズ（pt）です。</param>
    /// <param name="textPt">テキストサイズ（pt）です。</param>
    /// <returns>描画済みの UIImage です。</returns>
    private static UIImage Render(int count, string colorHex, nfloat sizePt, nfloat textPt)
    {
        var renderer = new UIGraphicsImageRenderer(new CGSize(sizePt, sizePt));
        var image = renderer.CreateImage(ctx =>
        {
            var rect = new CGRect(0, 0, sizePt, sizePt);

            // 1. 塗りつぶし円（カテゴリ色 α=0.85）
            var fillColor = ParseColor(colorHex).ColorWithAlpha(BackgroundAlpha);
            ctx.CGContext.SetFillColor(fillColor.CGColor);
            ctx.CGContext.FillEllipseInRect(rect);

            // 2. 白縁取り
            var strokeRect = new CGRect(
                StrokeWidthPt / 2,
                StrokeWidthPt / 2,
                sizePt - StrokeWidthPt,
                sizePt - StrokeWidthPt);
            ctx.CGContext.SetStrokeColor(UIColor.White.CGColor);
            ctx.CGContext.SetLineWidth(StrokeWidthPt);
            ctx.CGContext.StrokeEllipseInRect(strokeRect);

            // 3. 件数テキスト（白, Bold, 中央揃え）
            var text = new NSString(count.ToString());
            var paragraphStyle = new NSMutableParagraphStyle
            {
                Alignment = UITextAlignment.Center
            };
            var attrs = new UIStringAttributes
            {
                Font = UIFont.BoldSystemFontOfSize(textPt),
                ForegroundColor = UIColor.White,
                ParagraphStyle = paragraphStyle,
            };
            var textSize = text.GetSizeUsingAttributes(attrs);
            var textRect = new CGRect(
                (sizePt - textSize.Width) / 2,
                (sizePt - textSize.Height) / 2,
                textSize.Width,
                textSize.Height);
            text.DrawString(textRect, attrs);
        });

        return image;
    }
}
