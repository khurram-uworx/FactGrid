using System.Globalization;

namespace FactGrid.Services;

public static class ExcelDateHelper
{
    static readonly string[] DateFormats = ["M/d/yyyy h:mm:ss tt"];

    public static DateOnly? ParseDateText(string text)
    {
        if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }
}
