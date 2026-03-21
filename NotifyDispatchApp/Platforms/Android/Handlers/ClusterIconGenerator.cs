using Android.Graphics;
using Android.Util;
using Android.Gms.Maps.Model;
using Paint = Android.Graphics.Paint;

namespace NotifyDispatchApp.Platforms.Android.Handlers;

/// <summary>
/// クラスタマーカー用の丸アイコン Bitmap を生成する静的クラスです。
/// 件数に応じたサイズとカテゴリ色で円形マーカーを描画し、BitmapDescriptor を返します。
/// </summary>
public static class ClusterIconGenerator
{
    /// <summary>
    /// サイズ区分ごとの dp サイズとテキスト sp サイズの定義です。
    /// </summary>
    private static readonly (int MinCount, int SizeDp, int TextSp)[] SizeCategories =
    [
        (100, 128, 20),
        (50, 112, 18),
        (10, 96, 16),
        (2, 80, 14),
    ];

    /// <summary>
    /// デフォルトのサイズ区分（件数 2 未満のフォールバック）です。
    /// </summary>
    private const int DefaultSizeDp = 80;

    /// <summary>
    /// デフォルトのテキストサイズ（sp）です。
    /// </summary>
    private const int DefaultTextSp = 14;

    /// <summary>
    /// 背景色のアルファ値です（0.85 × 255 ≈ 217）。
    /// </summary>
    private const int BackgroundAlpha = 217;

    /// <summary>
    /// 白縁取りの太さ（dp）です。
    /// </summary>
    private const float StrokeWidthDp = 2f;

    /// <summary>
    /// Bitmap キャッシュです。キー: (件数, colorHex)。
    /// 同一件数・同一色の組み合わせはキャッシュから返します。
    /// </summary>
    private static readonly Dictionary<(int Count, string ColorHex), BitmapDescriptor> _cache = [];

    /// <summary>
    /// キャッシュ内の生 Bitmap を Recycle 用に保持するリストです。
    /// </summary>
    private static readonly List<Bitmap> _bitmaps = [];

    /// <summary>
    /// 指定件数とカテゴリ色でクラスタマーカー用の BitmapDescriptor を生成します。
    /// 同じサイズ区分・色の組み合わせはキャッシュから返します。
    /// </summary>
    /// <param name="count">クラスタに含まれるアイテム数です。</param>
    /// <param name="colorHex">カテゴリのテーマカラー（例: "#E53935"）です。</param>
    /// <param name="displayMetrics">dp/sp → px 変換に使用する DisplayMetrics です。</param>
    /// <returns>マーカーに設定する BitmapDescriptor です。</returns>
    public static BitmapDescriptor Create(int count, string colorHex, DisplayMetrics displayMetrics)
    {
        var (sizeDp, textSp, _) = GetSizeSpec(count);

        var cacheKey = (count, colorHex);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var descriptor = Render(count, colorHex, sizeDp, textSp, displayMetrics);
        _cache[cacheKey] = descriptor;
        return descriptor;
    }

    /// <summary>
    /// キャッシュ内の全 Bitmap を Recycle し、キャッシュをクリアします。
    /// ClusterMapHandler.DisconnectHandler から呼び出します。
    /// </summary>
    public static void ClearCache()
    {
        foreach (var bmp in _bitmaps)
        {
            if (!bmp.IsRecycled)
                bmp.Recycle();
        }
        _bitmaps.Clear();
        _cache.Clear();
    }

    /// <summary>
    /// 件数からサイズ区分（dp, sp, カテゴリキー）を決定します。
    /// </summary>
    /// <param name="count">アイテム数です。</param>
    /// <returns>サイズ dp、テキスト sp、区分キーのタプルです。</returns>
    private static (int SizeDp, int TextSp, int SizeCategory) GetSizeSpec(int count)
    {
        foreach (var (minCount, sizeDp, textSp) in SizeCategories)
        {
            if (count >= minCount)
                return (sizeDp, textSp, minCount);
        }
        return (DefaultSizeDp, DefaultTextSp, 2);
    }

    /// <summary>
    /// Android Canvas API を使用してクラスタアイコンの Bitmap を描画します。
    /// </summary>
    /// <param name="count">表示する件数です。</param>
    /// <param name="colorHex">背景の色コード（Hex）です。</param>
    /// <param name="sizeDp">アイコンサイズ（dp）です。</param>
    /// <param name="textSp">テキストサイズ（sp）です。</param>
    /// <param name="displayMetrics">dp/sp → px 変換用の DisplayMetrics です。</param>
    /// <returns>描画済みの BitmapDescriptor です。</returns>
    private static BitmapDescriptor Render(int count, string colorHex, int sizeDp, int textSp, DisplayMetrics displayMetrics)
    {
        var sizePx = (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, sizeDp, displayMetrics);
        var bitmap = Bitmap.CreateBitmap(sizePx, sizePx, Bitmap.Config.Argb8888!)!;
        var canvas = new Canvas(bitmap);

        var center = sizePx / 2f;
        var radius = center - 1f;

        // 1. 塗りつぶし円（カテゴリ色 α=0.85）
        using var fillPaint = new Paint(PaintFlags.AntiAlias);
        fillPaint.SetStyle(Paint.Style.Fill);
        fillPaint.Color = global::Android.Graphics.Color.ParseColor(colorHex);
        fillPaint.Alpha = BackgroundAlpha;
        canvas.DrawCircle(center, center, radius, fillPaint);

        // 2. 白縁取り
        var strokeWidthPx = TypedValue.ApplyDimension(ComplexUnitType.Dip, StrokeWidthDp, displayMetrics);
        using var strokePaint = new Paint(PaintFlags.AntiAlias);
        strokePaint.SetStyle(Paint.Style.Stroke);
        strokePaint.Color = global::Android.Graphics.Color.White;
        strokePaint.StrokeWidth = strokeWidthPx;
        canvas.DrawCircle(center, center, radius - strokeWidthPx / 2f, strokePaint);

        // 3. 件数テキスト（白, 中央揃え）
        var textSizePx = TypedValue.ApplyDimension(ComplexUnitType.Sp, textSp, displayMetrics);
        using var textPaint = new Paint(PaintFlags.AntiAlias);
        textPaint.Color = global::Android.Graphics.Color.White;
        textPaint.TextSize = textSizePx;
        textPaint.TextAlign = Paint.Align.Center;
        textPaint.SetTypeface(Typeface.DefaultBold);

        // テキストの垂直中央揃え: baseline オフセットを計算
        var textBounds = new global::Android.Graphics.Rect();
        var text = count.ToString();
        textPaint.GetTextBounds(text, 0, text.Length, textBounds);
        var yOffset = textBounds.Height() / 2f;
        canvas.DrawText(text, center, center + yOffset, textPaint);

        _bitmaps.Add(bitmap);
        return BitmapDescriptorFactory.FromBitmap(bitmap);
    }
}
