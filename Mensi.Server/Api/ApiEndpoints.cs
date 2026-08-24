using Mensi.Core.Api;
using Mensi.Core.Data;
using Mensi.Core.Domain;
using Mensi.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Server.Api;

public static class ApiEndpoints
{
    public static void MapMensiApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/overview", async (MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.BuildOverview(await LoadAsync(db, today))));

        api.MapGet("/trends", async (MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.BuildTrends(await LoadAsync(db, today))));

        api.MapGet("/calendar", async (int year, int month, MensiDbContext db, TodayProvider today) =>
            year is < 2000 or > 2100 || month is < 1 or > 12
                ? Problem("Érvénytelen év vagy hónap.")
                : Results.Ok(ReadModelBuilder.BuildCalendar(await LoadAsync(db, today), year, month)));

        api.MapGet("/chance", async (MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.BuildChance(await LoadAsync(db, today))));

        api.MapGet("/logs", async (DateOnly from, DateOnly to, MensiDbContext db, TodayProvider today) =>
        {
            if (to < from) return Problem("A tartomány vége nem lehet a kezdete előtt.");
            if (to.DayNumber - from.DayNumber > 400) return Problem("Legfeljebb 400 napos tartomány kérhető.");
            var input = await LoadAsync(db, today);
            return Results.Ok(new { Days = ReadModelBuilder.MapRange(input, from, to) });
        });

        api.MapGet("/logs/{date}", async (DateOnly date, MensiDbContext db, TodayProvider today) =>
            Results.Ok(ReadModelBuilder.MapOne(await LoadAsync(db, today), date)));

        api.MapPut("/logs/{date}", UpsertLogAsync);
        api.MapPut("/logs/{date}/intercourse", SetIntercourseAsync);
    }

    private static async Task<ModelInput> LoadAsync(MensiDbContext db, TodayProvider today)
    {
        var logs = await db.DailyLogs.AsNoTracking().Include(l => l.Intercourse)
            .OrderBy(l => l.Date).ToListAsync();
        var cycles = await db.Cycles.AsNoTracking().OrderBy(c => c.StartDate).ToListAsync();
        return new ModelInput(logs, cycles, today.Today);
    }

    private static IResult Problem(string detail) =>
        Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);

    private static async Task<IResult> UpsertLogAsync(
        DateOnly date, UpdateLogRequest request, MensiDbContext db, TodayProvider todayProvider,
        CurrentUser user, AuditWriter audit, CycleRecomputeService recompute, TimeProvider clock)
    {
        var today = todayProvider.Today;
        if (date > today) return Problem("Jövőbeli napra nem rögzíthető bejegyzés.");
        if (request.BbtCelsius is { IsSet: true, Value: not null and (< 35.00m or > 38.99m) })
            return Problem("A testhő 35,00 és 38,99 °C között adható meg.");
        if (request.CrampSeverity is { IsSet: true, Value: not null and (< 0 or > 3) })
            return Problem("A görcs erőssége 0 és 3 között adható meg.");
        if (request.Moods is { IsSet: true, Value: not null } moods
            && moods.Value!.Any(m => !Enum.IsDefined(m)))
            return Problem("Ismeretlen hangulat-érték.");
        foreach (var invalid in new[]
        {
            request.CervicalMucus is { IsSet: true, Value: not null } cm && !Enum.IsDefined(cm.Value!.Value),
            request.LhTest is { IsSet: true, Value: not null } lh && !Enum.IsDefined(lh.Value!.Value),
            request.CrampType is { IsSet: true, Value: not null } ct && !Enum.IsDefined(ct.Value!.Value),
            request.FlowIntensity is { IsSet: true, Value: not null } fi && !Enum.IsDefined(fi.Value!.Value),
        })
            if (invalid) return Problem("Ismeretlen enum-érték.");

        var log = await db.DailyLogs.Include(l => l.Intercourse)
            .SingleOrDefaultAsync(l => l.Date == date);
        var created = log is null;
        log ??= new DailyLog { Date = date, CreatedAt = clock.GetUtcNow() };

        var changes = new Dictionary<string, (object? Old, object? New)>();
        void Apply<T>(Patch<T> patch, string name, Func<DailyLog, T?> get, Action<DailyLog, T?> set)
        {
            if (!patch.IsSet) return;
            var old = get(log);
            if (Equals(old, patch.Value)) return;
            changes[name] = (old, patch.Value);
            set(log, patch.Value);
        }

        Apply(request.BbtCelsius, "bbtCelsius", l => l.BbtCelsius, (l, v) => l.BbtCelsius = v);
        Apply(request.CervicalMucus, "cervicalMucus", l => l.CervicalMucus, (l, v) => l.CervicalMucus = v);
        Apply(request.LhTest, "lhTest", l => l.LhTest, (l, v) => l.LhTest = v);
        Apply(request.CrampType, "crampType", l => l.CrampType, (l, v) => l.CrampType = v);
        Apply(request.CrampSeverity, "crampSeverity", l => l.CrampSeverity, (l, v) => l.CrampSeverity = v);
        Apply(request.FlowIntensity, "flowIntensity", l => l.FlowIntensity, (l, v) => l.FlowIntensity = v);
        if (request.PeriodStart.IsSet && log.PeriodStart != request.PeriodStart.Value)
        {
            changes["periodStart"] = (log.PeriodStart, request.PeriodStart.Value);
            log.PeriodStart = request.PeriodStart.Value;
        }
        if (request.Moods.IsSet)
        {
            var next = request.Moods.Value ?? [];
            if (!log.Moods.SequenceEqual(next))
            {
                changes["moods"] = (log.Moods.ToList(), next);
                log.Moods = next;
            }
        }

        // Konzisztencia: "nincs görcs" mellett nincs hely; hely csak erősséggel együtt értelmes.
        if (log.CrampSeverity is 0 && log.CrampType is not null)
        {
            changes["crampType"] = (log.CrampType, null);
            log.CrampType = null;
        }

        if (created && changes.Count == 0) return Results.Ok(ReadModelBuilder.MapOne(
            await LoadAsyncFor(db, date, todayProvider), date));

        if (changes.Count > 0)
        {
            log.UpdatedAt = clock.GetUtcNow();
            log.UpdatedBy = user.Email;
            if (created) db.DailyLogs.Add(log);
            audit.Add(user.Email, "log.upsert", date, changes);
            await db.SaveChangesAsync();
            await recompute.RecomputeAsync();
        }

        return Results.Ok(ReadModelBuilder.MapOne(await LoadAsyncFor(db, date, todayProvider), date));
    }

    private static async Task<IResult> SetIntercourseAsync(
        DateOnly date, SetIntercourseRequest request, MensiDbContext db, TodayProvider todayProvider,
        CurrentUser user, AuditWriter audit, CycleRecomputeService recompute, TimeProvider clock)
    {
        var today = todayProvider.Today;
        if (date > today) return Problem("Jövőbeli napra nem rögzíthető bejegyzés.");
        if (request.Events.Count > 6) return Problem("Naponta legfeljebb 6 esemény rögzíthető.");

        var log = await db.DailyLogs.Include(l => l.Intercourse)
            .SingleOrDefaultAsync(l => l.Date == date);
        if (log is null)
        {
            log = new DailyLog { Date = date, CreatedAt = clock.GetUtcNow() };
            db.DailyLogs.Add(log);
        }

        var old = log.Intercourse.Select(i => i.Protected).ToList();
        log.Intercourse.Clear();
        foreach (var ev in request.Events)
            log.Intercourse.Add(new IntercourseEvent
            {
                Date = date, Protected = ev.Protected, CreatedAt = clock.GetUtcNow(),
            });

        log.UpdatedAt = clock.GetUtcNow();
        log.UpdatedBy = user.Email;
        audit.Add(user.Email, "intercourse.set", date, new Dictionary<string, (object?, object?)>
        {
            ["intercourse"] = (old, request.Events.Select(e => e.Protected).ToList()),
        });
        await db.SaveChangesAsync();
        await recompute.RecomputeAsync();

        return Results.Ok(ReadModelBuilder.MapOne(await LoadAsyncFor(db, date, todayProvider), date));
    }

    private static async Task<ModelInput> LoadAsyncFor(
        MensiDbContext db, DateOnly around, TodayProvider today)
    {
        // A MapOne outlier-flagjéhez a nap ciklusának környezete kell — a teljes betöltés
        // egyszerű és ezen az adatméreten (évi ~365 sor) olcsó.
        var logs = await db.DailyLogs.AsNoTracking().Include(l => l.Intercourse)
            .OrderBy(l => l.Date).ToListAsync();
        var cycles = await db.Cycles.AsNoTracking().OrderBy(c => c.StartDate).ToListAsync();
        return new ModelInput(logs, cycles, today.Today);
    }
}
