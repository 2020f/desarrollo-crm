using Desarrollo.Application.Common;

namespace Desarrollo.Infrastructure.Services;

internal static class CalendarDateRules
{
    public const string DefaultTimeZoneId = "America/Santo_Domingo";

    // SQL Server datetime2 does not retain DateTime.Kind. Timed records are stored as UTC;
    // date-only planning values are civil dates and must never be shifted through UTC.
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime Civil(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    public static TimeZoneInfo Zone(string? value)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(value) ? DefaultTimeZoneId : value); }
        catch (TimeZoneNotFoundException) { throw new ValidationException("La zona horaria no es válida."); }
        catch (InvalidTimeZoneException) { throw new ValidationException("La zona horaria no es válida."); }
    }

    public static (DateTime Start, DateTime End) Range(DateTime start, DateTime end)
    {
        start = Utc(start); end = Utc(end);
        if (end <= start || end - start > TimeSpan.FromDays(366))
            throw new ValidationException("El intervalo debe ser positivo y no superar 366 días.");
        return (start, end);
    }
}
