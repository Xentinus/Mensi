using Mensi.Core.Options;
using Microsoft.Extensions.Options;

namespace Mensi.Core.Services;

public sealed class TodayProvider(TimeProvider clock, IOptions<DisplayOptions> options)
{
    public DateOnly Today
    {
        get
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
        }
    }
}
