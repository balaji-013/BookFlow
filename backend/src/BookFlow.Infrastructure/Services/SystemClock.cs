using BookFlow.Application.Common.Interfaces;

namespace BookFlow.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo Ist = ResolveIst();

    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime NowIst => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);
    public DateOnly TodayIst => DateOnly.FromDateTime(NowIst);

    private static TimeZoneInfo ResolveIst()
    {
        foreach (var id in new[] { "Asia/Kolkata", "India Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { /* try next */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromMinutes(330), "India Standard Time", "India Standard Time");
    }
}
