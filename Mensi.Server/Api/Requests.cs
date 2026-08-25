using Mensi.Core.Api;
using Mensi.Core.Domain;

namespace Mensi.Server.Api;

/// <summary>Mezőnkénti részleges upsert: jelen lévő kulcs null-lal = törlés, hiányzó = érintetlen.</summary>
public sealed record UpdateLogRequest
{
    public Patch<decimal?> BbtCelsius { get; init; } = new();
    public Patch<CervicalMucus?> CervicalMucus { get; init; } = new();
    public Patch<LhTest?> LhTest { get; init; } = new();

    /// <summary>A tesztcsík/kontrollcsík arány 0–1 skálán. Beállításakor a háromértékű
    /// <see cref="LhTest"/> ebből származik — a kliens csak ezt a mezőt küldi.</summary>
    public Patch<decimal?> LhValue { get; init; } = new();
    public Patch<CrampType?> CrampType { get; init; } = new();
    public Patch<short?> CrampSeverity { get; init; } = new();
    public Patch<FlowIntensity?> FlowIntensity { get; init; } = new();
    public Patch<bool> PeriodStart { get; init; } = new();
    public Patch<List<Mood>?> Moods { get; init; } = new();
}

public sealed record IntercourseEventRequest(bool? Protected);
public sealed record SetIntercourseRequest(List<IntercourseEventRequest> Events);
