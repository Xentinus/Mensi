export type CervicalMucus = 'dry' | 'sticky' | 'creamy' | 'eggWhite'
export type LhTest = 'negative' | 'positive' | 'peak'
export type CrampType = 'abdomen' | 'back' | 'breast'
export type FlowIntensity = 'none' | 'spotting' | 'light' | 'medium' | 'heavy'
export type Mood = 'cheerful' | 'calm' | 'irritable' | 'tired' | 'sad' | 'anxious' | 'longing'
export type TimingLabel = 'weak' | 'medium' | 'good'
export type ConfidenceLevel = 'low' | 'medium' | 'high'
export type DayCategory =
  | 'preCycle' | 'menstruation' | 'follicular' | 'fertile'
  | 'ovulation' | 'luteal' | 'predictedPeriod' | 'unknown'

export interface IntercourseEvent { id: number; protected: boolean | null }

export interface DailyLog {
  date: string
  bbtCelsius: number | null
  bbtOutlier: boolean
  cervicalMucus: CervicalMucus | null
  lhTest: LhTest | null
  /** A tesztcsík/kontrollcsík arány 0–1 skálán; a lhTest ebből származtatott. */
  lhValue: number | null
  crampType: CrampType | null
  crampSeverity: number | null
  flowIntensity: FlowIntensity | null
  periodStart: boolean
  moods: Mood[]
  intercourse: IntercourseEvent[]
  updatedAt: string | null
  updatedBy: string | null
}

export interface LogPatch {
  bbtCelsius?: number | null
  cervicalMucus?: CervicalMucus | null
  lhTest?: LhTest | null
  lhValue?: number | null
  crampType?: CrampType | null
  crampSeverity?: number | null
  flowIntensity?: FlowIntensity | null
  periodStart?: boolean
  moods?: Mood[] | null
}

export interface DateWindow { from: string; to: string }
export interface Phase { key: DayCategory; label: string; totalDays: number; elapsedDays: number; remainingDays: number }
export interface StripDay { date: string; cycleDay: number | null; category: DayCategory; isToday: boolean }
export interface TimingDay { date: string; cycleDay: number; intercourseCount: number; isOvulationWindow: boolean; isFuture: boolean }
export interface Timing {
  label: TimingLabel; chancePercent: number; daysRemaining: number
  intercourseTotal: number; windowDays: TimingDay[]
}

export interface Overview {
  today: string
  isEmpty: boolean
  cycle: { day: number; startDate: string } | null
  phase: Phase | null
  headline: string | null
  ovulationWindow: DateWindow | null
  nextPeriodWindow: DateWindow | null
  confidence: ConfidenceLevel | null
  pregnancyHint: string | null
  measurementHint: string | null
  strip: { from: string; to: string; days: StripDay[] } | null
  timing: Timing | null
  todayLog: DailyLog | null
  yesterdayLog: DailyLog | null
}

export interface TimingSummary { label: TimingLabel; chancePercent: number }
export interface TrendCycle {
  startDate: string; lengthDays: number; deviationFromAverage: number
  lutealLength: number | null; anovulatory: boolean; timing: TimingSummary
}
export interface BbtRow {
  date: string; cycleDay: number; value: number | null; deltaFromCoverline: number | null
  isOutlier: boolean; aboveCoverline: boolean
  marks: { cervicalMucus: CervicalMucus | null; lhTest: LhTest | null; lhValue: number | null }
}
export interface Trends {
  stats: {
    averageLength: number; minLength: number; maxLength: number
    stdDev: number; averageLuteal: number | null; loggedPercent: number
  } | null
  cycles: TrendCycle[]
  bbt: {
    coverline: number | null; ovulationConfirmed: boolean; confirmedOvulationDate: string | null
    excludedOutlierCount: number; missingDayCount: number; rows: BbtRow[]
  } | null
}

export interface CalendarDay {
  date: string; cycleDay: number | null; category: DayCategory
  hasBbt: boolean; intercourseCount: number; hasAnyEntry: boolean; isToday: boolean
  /** Előrevetített (a nyitott ciklus becsült menstruációja utáni) nap. */
  isProjected: boolean
}
export interface CalendarMonth {
  month: string
  range: { firstMonth: string; lastMonth: string }
  cycleDayOfToday: number | null
  hasData: boolean
  days: CalendarDay[]
}

export interface FertileDay { date: string; cycleDay: number; intercourseCount: number; isFuture: boolean; isToday: boolean }
export interface Chance {
  isEmpty: boolean
  timing: TimingSummary | null
  explanation: string | null
  confidenceNote: string | null
  fertileWindow: {
    daysRemaining: number; ovulationWindowTotal: number; ovulationWindowElapsed: number
    days: FertileDay[]
  } | null
  whatIfHint: string | null
  history: { goodCount: number; totalCount: number; cycles: { startDate: string; timing: TimingSummary }[] } | null
}

export interface ImportCycle { startDate: string; periodDays: number }

export interface ImportResult {
  applied: boolean
  cyclesFound: number
  from: string | null
  to: string | null
  lhTestCount: number
  daysWritten: number
  fieldsSkipped: number
  bbtCount: number
  intercourseDays: number
  mucusDays: number
  symptomMoodDays: number
  cycles: ImportCycle[]
  warnings: string[]
}
