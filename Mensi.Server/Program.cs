using System.Text.Json;
using System.Text.Json.Serialization;
using Mensi.Core.Api;
using Mensi.Core.Data;
using Mensi.Core.Middleware;
using Mensi.Core.Options;
using Mensi.Core.Services;
using Mensi.Server.Api;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddDbContext<MensiDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=mensi;Username=mensi;Password=mensi"));

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.SerializerOptions.Converters.Add(new PatchJsonConverterFactory());
    });

    builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection(DisplayOptions.SectionName));
    builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection(AuditOptions.SectionName));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TodayProvider>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<CurrentUser>();
    builder.Services.AddScoped<AuditWriter>();
    builder.Services.AddScoped<CycleRecomputeService>();
    builder.Services.AddScoped<PcReportImporter>();
    builder.Services.AddHostedService<AuditRetentionService>();

    // Cloudflare Access: élesben kötelező — verifikáció nélkül a host nem indulhat el.
    // A CloudflareAccess__Enabled=false (CF_ACCESS_ENABLED) kizárólag lokális teszthez való.
    var accessOptions = builder.Configuration.GetSection(CloudflareAccessOptions.SectionName)
        .Get<CloudflareAccessOptions>() ?? new CloudflareAccessOptions();
    if (accessOptions.Enabled && !accessOptions.IsConfigured && !builder.Environment.IsDevelopment())
        throw new InvalidOperationException(
            "CloudflareAccess:TeamDomain és CloudflareAccess:Audience kötelező Development-en kívül "
            + "(CF_ACCESS_* env változók, ld. .env.example). Lokális teszthez a CF_ACCESS_ENABLED=false "
            + "kapcsolja ki az ellenőrzést — élesben ezt soha ne használd.");
    builder.Services.Configure<CloudflareAccessOptions>(
        builder.Configuration.GetSection(CloudflareAccessOptions.SectionName));
    builder.Services.AddHttpClient(CloudflareAccessKeyStore.HttpClientName);
    builder.Services.AddSingleton<CloudflareAccessKeyStore>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // A /health az Access előtt: a konténer belülről, assertion nélkül ellenőrzi magát.
    app.MapGet("/health", () => Results.Text("OK"));

    if (accessOptions.Enabled && accessOptions.IsConfigured)
        app.UseWhen(
            context => context.Request.Path != "/health",
            gated => gated.UseMiddleware<CloudflareAccessMiddleware>());
    else if (!accessOptions.Enabled)
        app.Logger.LogWarning(
            "Cloudflare Access ellenőrzés EXPLICIT KIKAPCSOLVA (CloudflareAccess__Enabled=false) "
            + "— kizárólag lokális teszthez, élesben SOHA!");
    else
        app.Logger.LogWarning("Cloudflare Access ellenőrzés KIKAPCSOLVA (nincs konfigurálva) — csak Development!");

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.MapMensiApi();

    // Ismeretlen /api útvonal 404, nem az SPA index — a hiányzó endpoint ne adjon 200 HTML-t.
    app.MapFallback("/api/{**rest}", () => Results.NotFound());
    if (File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
        app.MapFallbackToFile("index.html");

    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<MensiDbContext>().Database.MigrateAsync();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "A host indítása elhasalt");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
