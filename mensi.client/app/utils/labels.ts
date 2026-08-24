import type { CervicalMucus, ConfidenceLevel, CrampType, DayCategory, FlowIntensity, LhTest, Mood, TimingLabel } from '~/types/api'

export const MUCUS_LABELS: Record<CervicalMucus, string> =
  { dry: 'Száraz', sticky: 'Ragadós', creamy: 'Nedves', eggWhite: 'Nyúlós' }
export const MUCUS_ORDER: CervicalMucus[] = ['dry', 'sticky', 'creamy', 'eggWhite']

export const LH_LABELS: Record<LhTest, string> = { negative: 'Negatív', positive: 'Pozitív', peak: 'Csúcs' }
export const LH_ORDER: LhTest[] = ['negative', 'positive', 'peak']
export const LH_NOTES: Record<LhTest, string> = {
  negative: 'halvány vagy nincs csík', positive: 'a tesztcsík látható', peak: 'a legsötétebb eddig',
}

export const CRAMP_TYPE_LABELS: Record<CrampType, string> = { abdomen: 'Alhas', back: 'Derék', breast: 'Mell' }
export const CRAMP_TYPE_ORDER: CrampType[] = ['abdomen', 'back', 'breast']
export const CRAMP_SEVERITY_LABELS = ['Nincs', 'Enyhe', 'Közepes', 'Erős'] as const

export const FLOW_LABELS: Record<FlowIntensity, string> =
  { none: 'Nincs', spotting: 'Pecsételő', light: 'Enyhe', medium: 'Közepes', heavy: 'Erős' }
export const FLOW_ORDER: FlowIntensity[] = ['none', 'spotting', 'light', 'medium', 'heavy']

export const MOOD_LABELS: Record<Mood, string> = {
  cheerful: 'Vidám', calm: 'Nyugodt', irritable: 'Ingerlékeny', tired: 'Fáradt',
  sad: 'Szomorú', anxious: 'Szorongó', longing: 'Vágyakozó',
}
export const MOOD_EMOJI: Record<Mood, string> = {
  cheerful: '😊', calm: '😌', irritable: '😠', tired: '😴', sad: '😢', anxious: '😟', longing: '😍',
}
export const MOOD_ORDER: Mood[] = ['cheerful', 'calm', 'irritable', 'tired', 'sad', 'anxious', 'longing']

export const TIMING_LABELS: Record<TimingLabel, string> = { weak: 'Gyenge', medium: 'Közepes', good: 'Jó' }
export const CONFIDENCE_LABELS: Record<ConfidenceLevel, string> = { low: 'alacsony', medium: 'közepes', high: 'magas' }

/** Az 5 hetes Ma-sáv cellaszínei (prototípus: maPanel.cycleDays). */
export const STRIP_COLORS: Record<DayCategory, { bg: string; fg: string }> = {
  preCycle: { bg: '#f7f8fd', fg: '#9aa0bd' },
  menstruation: { bg: '#6f71d6', fg: '#ffffff' },
  follicular: { bg: '#f0f2fb', fg: '#6a7095' },
  fertile: { bg: '#c6d6ff', fg: '#26365f' },
  ovulation: { bg: '#5a5cd6', fg: '#ffffff' },
  luteal: { bg: '#e8eaf6', fg: '#4a4f75' },
  predictedPeriod: { bg: '#dcdef4', fg: '#4a4f75' },
  unknown: { bg: '#f0f2fb', fg: '#6a7095' },
}

/** A havi naptár cellaszínei (prototípus: calCells). */
export const CAL_COLORS: Record<DayCategory, { bg: string; fg: string }> = {
  preCycle: { bg: '#f7f8fd', fg: '#545a7a' },
  menstruation: { bg: '#8386e6', fg: '#20214d' },
  follicular: { bg: '#f5f7fe', fg: '#464b6b' },
  fertile: { bg: '#cfdcff', fg: '#26365f' },
  ovulation: { bg: '#b1b2ff', fg: '#2c2d63' },
  luteal: { bg: '#eaecf4', fg: '#464b6b' },
  predictedPeriod: { bg: '#dcdef4', fg: '#4a4f75' },
  unknown: { bg: '#f7f8fd', fg: '#545a7a' },
}

export const CATEGORY_LEGEND: { key: DayCategory; label: string }[] = [
  { key: 'menstruation', label: 'Menstruáció' },
  { key: 'fertile', label: 'Termékeny' },
  { key: 'ovulation', label: 'Ovuláció' },
  { key: 'luteal', label: 'Luteális' },
  { key: 'predictedPeriod', label: 'Becsült mens' },
]

/** A napló mezősorrendje — a sheet lépései és a listák közös vázát adja. */
export const FIELD_ORDER = ['bbt', 'mucus', 'lh', 'cramp', 'flow', 'intercourse', 'mood'] as const
export type FieldKey = (typeof FIELD_ORDER)[number]
export const FIELD_LABELS: Record<FieldKey, string> = {
  bbt: 'Testhő', mucus: 'Nyák', lh: 'LH-teszt', cramp: 'Görcs',
  flow: 'Folyás', intercourse: 'Együttlét', mood: 'Hangulat',
}
