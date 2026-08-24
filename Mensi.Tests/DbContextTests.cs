using Mensi.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Tests;

[Collection("postgres")]
public class DbContextTests(PostgresFixture fixture)
{
    [Fact]
    public async Task DailyLog_roundtrips_with_moods_and_intercourse()
    {
        await fixture.ResetAsync();
        var date = new DateOnly(2026, 8, 23);
        await using (var db = fixture.CreateContext())
        {
            db.DailyLogs.Add(new DailyLog
            {
                Date = date,
                BbtCelsius = 36.36m,
                CervicalMucus = CervicalMucus.EggWhite,
                LhTest = LhTest.Positive,
                CrampType = CrampType.Abdomen,
                CrampSeverity = 2,
                FlowIntensity = FlowIntensity.None,
                PeriodStart = false,
                Moods = [Mood.Cheerful, Mood.Longing],
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = "a@b.hu",
                Intercourse = [new IntercourseEvent { Date = date, Protected = false, CreatedAt = DateTimeOffset.UtcNow }]
            });
            await db.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext();
        var log = await read.DailyLogs.Include(l => l.Intercourse).SingleAsync(l => l.Date == date);
        Assert.Equal(36.36m, log.BbtCelsius);
        Assert.Equal([Mood.Cheerful, Mood.Longing], log.Moods);
        Assert.Single(log.Intercourse);
        Assert.False(log.Intercourse[0].Protected);
    }
}
