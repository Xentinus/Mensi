using Mensi.Core.Domain;

namespace Mensi.Core.Api;

public sealed record IntercourseDto(long Id, bool? Protected);

public sealed record DailyLogDto(
    DateOnly Date, decimal? BbtCelsius, bool BbtOutlier,
    CervicalMucus? CervicalMucus, LhTest? LhTest,
    CrampType? CrampType, short? CrampSeverity, FlowIntensity? FlowIntensity,
    bool PeriodStart, IReadOnlyList<Mood> Moods, IReadOnlyList<IntercourseDto> Intercourse,
    DateTimeOffset? UpdatedAt, string? UpdatedBy);

public sealed record WindowDto(DateOnly From, DateOnly To);
public sealed record CycleInfoDto(int Day, DateOnly StartDate);
public sealed record PhaseDto(DayCategory Key, string Label, int TotalDays, int ElapsedDays, int RemainingDays);
public sealed record StripDayDto(DateOnly Date, int? CycleDay, DayCategory Category, bool IsToday);
public sealed record StripDto(DateOnly From, DateOnly To, IReadOnlyList<StripDayDto> Days);
public sealed record TimingDayDto(DateOnly Date, int CycleDay, int IntercourseCount, bool IsOvulationWindow, bool IsFuture);
public sealed record TimingDto(TimingLabel Label, double ChancePercent, int DaysRemaining,
    int IntercourseTotal, IReadOnlyList<TimingDayDto> WindowDays);

public sealed record OverviewDto(
    DateOnly Today, bool IsEmpty, CycleInfoDto? Cycle, PhaseDto? Phase, string? Headline,
    WindowDto? OvulationWindow, WindowDto? NextPeriodWindow, ConfidenceLevel? Confidence,
    string? PregnancyHint, StripDto? Strip, TimingDto? Timing,
    DailyLogDto? TodayLog, DailyLogDto? YesterdayLog);

public sealed record TimingSummaryDto(TimingLabel Label, double ChancePercent);
public sealed record TrendsStatsDto(double AverageLength, int MinLength, int MaxLength,
    double StdDev, double? AverageLuteal, int LoggedPercent);
public sealed record TrendCycleDto(DateOnly StartDate, int LengthDays, int DeviationFromAverage,
    int? LutealLength, bool Anovulatory, TimingSummaryDto Timing);
public sealed record BbtMarksDto(CervicalMucus? CervicalMucus, LhTest? LhTest);
public sealed record BbtRowDto(DateOnly Date, int CycleDay, decimal? Value, decimal? DeltaFromCoverline,
    bool IsOutlier, bool AboveCoverline, BbtMarksDto Marks);
public sealed record TrendsBbtDto(decimal? Coverline, bool OvulationConfirmed,
    DateOnly? ConfirmedOvulationDate, int ExcludedOutlierCount, int MissingDayCount,
    IReadOnlyList<BbtRowDto> Rows);
public sealed record TrendsDto(TrendsStatsDto? Stats, IReadOnlyList<TrendCycleDto> Cycles, TrendsBbtDto? Bbt);

public sealed record MonthRangeDto(string FirstMonth, string LastMonth);
public sealed record CalendarDayDto(DateOnly Date, int? CycleDay, DayCategory Category,
    bool HasBbt, int IntercourseCount, bool HasAnyEntry, bool IsToday);
public sealed record CalendarDto(string Month, MonthRangeDto Range, int? CycleDayOfToday,
    bool HasData, IReadOnlyList<CalendarDayDto> Days);

public sealed record FertileDayDto(DateOnly Date, int CycleDay, int IntercourseCount, bool IsFuture, bool IsToday);
public sealed record FertileWindowDto(int DaysRemaining, int OvulationWindowTotal,
    int OvulationWindowElapsed, IReadOnlyList<FertileDayDto> Days);
public sealed record ChanceHistoryCycleDto(DateOnly StartDate, TimingSummaryDto Timing);
public sealed record ChanceHistoryDto(int GoodCount, int TotalCount, IReadOnlyList<ChanceHistoryCycleDto> Cycles);
public sealed record ChanceDto(bool IsEmpty, TimingSummaryDto? Timing, string? Explanation,
    string? ConfidenceNote, FertileWindowDto? FertileWindow, string? WhatIfHint, ChanceHistoryDto? History);

public sealed record ImportCycleDto(DateOnly StartDate, int PeriodDays);

public sealed record ImportResultDto(
    bool Applied, int CyclesFound, DateOnly? From, DateOnly? To,
    int LhTestCount, int DaysWritten, int FieldsSkipped,
    int BbtCount, int IntercourseDays, int MucusDays, int SymptomMoodDays,
    IReadOnlyList<ImportCycleDto> Cycles, IReadOnlyList<string> Warnings);
