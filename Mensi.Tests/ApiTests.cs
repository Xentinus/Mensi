using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Mensi.Tests;

public sealed class MensiApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development"); // Access kikapcsolva, dev fallback identitás
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }
}

[Collection("postgres")]
public class ApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    private MensiApiFactory _factory = null!;
    private HttpClient _client = null!;
    private static readonly DateOnly Today =
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest")).DateTime);

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();
        _factory = new MensiApiFactory(fixture.ConnectionString);
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    private async Task<HttpResponseMessage> PutLog(DateOnly date, object body) =>
        await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}", body);

    [Fact]
    public async Task Health_is_reachable_without_assertion()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Partial_upsert_touches_only_sent_fields_and_audits()
    {
        var date = Today.AddDays(-1);
        Assert.Equal(HttpStatusCode.OK, (await PutLog(date, new { bbtCelsius = 36.42 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PutLog(date, new { cervicalMucus = "eggWhite" })).StatusCode);

        var log = await _client.GetFromJsonAsync<JsonElement>($"/api/logs/{date:yyyy-MM-dd}");
        Assert.Equal(36.42m, log.GetProperty("bbtCelsius").GetDecimal());
        Assert.Equal("eggWhite", log.GetProperty("cervicalMucus").GetString());

        await using var db = fixture.CreateContext();
        var audits = await db.AuditEntries.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => Assert.Equal("dev@localhost", a.Email));
        Assert.Contains("bbtCelsius", audits[0].ChangesJson);
    }

    [Fact]
    public async Task Null_clears_a_field()
    {
        var date = Today.AddDays(-1);
        await PutLog(date, new { bbtCelsius = 36.42 });
        await PutLog(date, new { bbtCelsius = (decimal?)null });
        var log = await _client.GetFromJsonAsync<JsonElement>($"/api/logs/{date:yyyy-MM-dd}");
        Assert.Equal(JsonValueKind.Null, log.GetProperty("bbtCelsius").ValueKind);
    }

    [Fact]
    public async Task Cramp_severity_zero_clears_type()
    {
        var date = Today.AddDays(-1);
        await PutLog(date, new { crampType = "abdomen", crampSeverity = 2 });
        await PutLog(date, new { crampSeverity = 0 });
        var log = await _client.GetFromJsonAsync<JsonElement>($"/api/logs/{date:yyyy-MM-dd}");
        Assert.Equal(JsonValueKind.Null, log.GetProperty("crampType").ValueKind);
    }

    [Fact]
    public async Task Cramp_type_and_severity_zero_together_do_not_fabricate_audit_history()
    {
        // Üres napon egyetlen kérésben érkezik crampType és crampSeverity=0 együtt: a konzisztencia-
        // szabály törli a crampType-ot, de a valódi, kérés előtti állapot null volt — a perzisztált
        // átmenet null→null, ami nem auditálható eseményként, csak a crampSeverity null→0 az.
        var date = Today.AddDays(-1);
        var response = await PutLog(date, new { crampType = "abdomen", crampSeverity = 0 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var log = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, log.GetProperty("crampType").ValueKind);

        await using var db = fixture.CreateContext();
        var audits = await db.AuditEntries.OrderBy(a => a.Id).ToListAsync();
        var audit = Assert.Single(audits);
        using var changes = JsonDocument.Parse(audit.ChangesJson);
        Assert.False(changes.RootElement.TryGetProperty("crampType", out _),
            "a crampType kulcsnak hiányoznia kell: a valódi átmenet null->null volt, nem auditálható esemény");
        Assert.True(changes.RootElement.TryGetProperty("crampSeverity", out var severityChange));
        Assert.Equal(JsonValueKind.Null, severityChange.GetProperty("old").ValueKind);
        Assert.Equal(0, severityChange.GetProperty("new").GetInt32());
    }

    [Fact]
    public async Task Period_start_builds_cycles_and_writes_prediction_once()
    {
        var first = Today.AddDays(-56);
        await PutLog(first, new { periodStart = true, flowIntensity = "medium" });
        await PutLog(first.AddDays(28), new { periodStart = true, flowIntensity = "medium" });

        await using var db = fixture.CreateContext();
        var cycles = await db.Cycles.OrderBy(c => c.StartDate).ToListAsync();
        Assert.Equal(2, cycles.Count);
        Assert.Equal(28, cycles[0].LengthDays);
        Assert.Null(cycles[0].PredictedLengthDays);   // az elsőnek nincs előzménye
        Assert.Equal(28, cycles[1].PredictedLengthDays); // shrink(28,16,n=1,x̄=28) = 28
        Assert.Null(cycles[1].LengthDays);
    }

    [Theory]
    [InlineData("""{"bbtCelsius": 34.2}""")]
    [InlineData("""{"crampSeverity": 5}""")]
    [InlineData("""{"cervicalMucus": "vaporous"}""")]
    public async Task Invalid_values_are_rejected(string body)
    {
        var date = Today.AddDays(-1);
        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PutAsync($"/api/logs/{date:yyyy-MM-dd}", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Future_date_is_rejected()
    {
        var response = await PutLog(Today.AddDays(2), new { bbtCelsius = 36.42 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Intercourse_put_replaces_the_day_and_caps_at_six()
    {
        var date = Today.AddDays(-1);
        var two = await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}/intercourse",
            new { events = new[] { new { @protected = (bool?)false }, new { @protected = (bool?)true } } });
        Assert.Equal(HttpStatusCode.OK, two.StatusCode);

        var one = await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}/intercourse",
            new { events = new[] { new { @protected = (bool?)null } } });
        var log = await one.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, log.GetProperty("intercourse").GetArrayLength());

        var seven = await _client.PutAsJsonAsync($"/api/logs/{date:yyyy-MM-dd}/intercourse",
            new { events = Enumerable.Repeat(new { @protected = (bool?)false }, 7).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, seven.StatusCode);
    }

    [Fact]
    public async Task Intercourse_put_with_missing_events_is_rejected()
    {
        // {} body -> request.Events deserializál null-ra (nincs required constructor-tag), a
        // Count-hozzáférés NRE-t dobna a null-guard nélkül.
        var date = Today.AddDays(-1);
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var response = await _client.PutAsync($"/api/logs/{date:yyyy-MM-dd}/intercourse", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Overview_goes_from_empty_to_predicted()
    {
        var empty = await _client.GetFromJsonAsync<JsonElement>("/api/overview");
        Assert.True(empty.GetProperty("isEmpty").GetBoolean());

        var first = Today.AddDays(-56);
        await PutLog(first, new { periodStart = true, flowIntensity = "medium" });
        await PutLog(first.AddDays(28), new { periodStart = true, flowIntensity = "medium" });
        await PutLog(first.AddDays(56), new { periodStart = true, flowIntensity = "medium" });

        var full = await _client.GetFromJsonAsync<JsonElement>("/api/overview");
        Assert.False(full.GetProperty("isEmpty").GetBoolean());
        Assert.Equal(35, full.GetProperty("strip").GetProperty("days").GetArrayLength());
        Assert.NotEqual(JsonValueKind.Null, full.GetProperty("ovulationWindow").ValueKind);

        var trends = await _client.GetFromJsonAsync<JsonElement>("/api/trends");
        Assert.Equal(2, trends.GetProperty("cycles").GetArrayLength());

        var calendar = await _client.GetFromJsonAsync<JsonElement>(
            $"/api/calendar?year={Today.Year}&month={Today.Month}");
        Assert.True(calendar.GetProperty("hasData").GetBoolean());

        var chance = await _client.GetFromJsonAsync<JsonElement>("/api/chance");
        Assert.False(chance.GetProperty("isEmpty").GetBoolean());
    }

    [Fact]
    public async Task Unknown_api_path_is_404_not_spa()
    {
        var response = await _client.GetAsync("/api/nincs-ilyen");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static MultipartFormDataContent FixtureUpload()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pc-report-fixture.pdf"));
        var content = new MultipartFormDataContent();
        var filePart = new ByteArrayContent(bytes);
        filePart.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        content.Add(filePart, "file", "pc-report.pdf");
        return content;
    }

    [Fact]
    public async Task Pc_report_import_dry_run_previews_then_apply_is_idempotent()
    {
        using var dryContent = FixtureUpload();
        var dry = await _client.PostAsync("/api/import/pc-report?dryRun=true", dryContent);
        Assert.Equal(HttpStatusCode.OK, dry.StatusCode);
        var preview = await dry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(preview.GetProperty("applied").GetBoolean());
        Assert.Equal(5, preview.GetProperty("cyclesFound").GetInt32());
        Assert.True(preview.GetProperty("daysWritten").GetInt32() > 0);

        await using (var db = fixture.CreateContext())
            Assert.Equal(0, await db.DailyLogs.CountAsync()); // dry-run nem ír

        using var applyContent = FixtureUpload();
        var apply = await _client.PostAsync("/api/import/pc-report", applyContent);
        var result = await apply.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("applied").GetBoolean());

        await using (var db = fixture.CreateContext())
        {
            Assert.Equal(5, await db.DailyLogs.CountAsync(l => l.PeriodStart));
            Assert.True(await db.Cycles.CountAsync() >= 4);
            Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Action == "import.pcReport"));

            // grafikonról jövő napi jelek (a fixture 2. oldala, Feb 28-i ciklus)
            var mar9 = await db.DailyLogs.SingleAsync(l => l.Date == new DateOnly(2026, 3, 9));
            Assert.NotNull(mar9.BbtCelsius); // ~36,60 (±0,02 a grafikon-visszamérés miatt)
            Assert.Equal(Mensi.Core.Domain.LhTest.Negative, mar9.LhTest);
            var mar12 = await db.DailyLogs.Include(l => l.Intercourse)
                .SingleAsync(l => l.Date == new DateOnly(2026, 3, 12));
            Assert.Equal(Mensi.Core.Domain.CervicalMucus.EggWhite, mar12.CervicalMucus);
            Assert.Equal([Mensi.Core.Domain.Mood.Longing], mar12.Moods);
            var mar11 = await db.DailyLogs.Include(l => l.Intercourse)
                .SingleAsync(l => l.Date == new DateOnly(2026, 3, 11));
            Assert.Single(mar11.Intercourse);
            Assert.False(mar11.Intercourse[0].Protected);
            var mar1 = await db.DailyLogs.SingleAsync(l => l.Date == new DateOnly(2026, 3, 1));
            Assert.Equal((short)1, mar1.CrampSeverity);
            var mar19 = await db.DailyLogs.SingleAsync(l => l.Date == new DateOnly(2026, 3, 19));
            Assert.Equal(Mensi.Core.Domain.FlowIntensity.Spotting, mar19.FlowIntensity);
        }

        using var againContent = FixtureUpload();
        var again = await _client.PostAsync("/api/import/pc-report", againContent);
        var second = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(second.GetProperty("applied").GetBoolean()); // minden mező már kitöltött
        Assert.Equal(0, second.GetProperty("daysWritten").GetInt32());
    }

    [Fact]
    public async Task Pc_report_import_respects_from_date()
    {
        using var dryContent = FixtureUpload();
        var dry = await _client.PostAsync("/api/import/pc-report?dryRun=true&from=2026-01-24", dryContent);
        var preview = await dry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, preview.GetProperty("cyclesFound").GetInt32());
        Assert.Equal("2026-01-24", preview.GetProperty("from").GetString());
        Assert.Equal(3, preview.GetProperty("cycles").GetArrayLength());

        using var applyContent = FixtureUpload();
        await _client.PostAsync("/api/import/pc-report?from=2026-01-24", applyContent);
        await using var db = fixture.CreateContext();
        Assert.Equal(3, await db.DailyLogs.CountAsync(l => l.PeriodStart));
        Assert.Equal(0, await db.DailyLogs.CountAsync(l => l.Date < new DateOnly(2026, 1, 24)));
    }

    [Fact]
    public async Task Pc_report_import_rejects_non_pdf()
    {
        using var content = new MultipartFormDataContent();
        var filePart = new ByteArrayContent("nem pdf"u8.ToArray());
        content.Add(filePart, "file", "x.pdf");
        var response = await _client.PostAsync("/api/import/pc-report", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
