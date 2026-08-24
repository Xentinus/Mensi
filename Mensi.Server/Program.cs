var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => Results.Text("OK"));

app.Run();

// A WebApplicationFactory-nak kell hivatkozási pont az integrációs tesztekhez.
public partial class Program;
