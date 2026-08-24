using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<Mensi.Core.Data.MensiDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")
        ?? "Host=localhost;Port=5432;Database=mensi;Username=mensi;Password=mensi"));

var app = builder.Build();

app.MapGet("/health", () => Results.Text("OK"));

app.Run();

// A WebApplicationFactory-nak kell hivatkozási pont az integrációs tesztekhez.
public partial class Program;
