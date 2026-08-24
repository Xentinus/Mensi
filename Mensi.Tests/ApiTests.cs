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
}
