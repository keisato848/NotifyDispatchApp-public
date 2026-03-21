using System.Globalization;

namespace NotifyDispatchApp;

/// <summary>
/// bool値を反転するコンバーターです。
/// </summary>
public class InvertedBoolConverter : IValueConverter
{
    /// <summary>
    /// bool値を反転して返します。
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    /// <summary>
    /// 反転されたbool値を元に戻して返します。
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}
