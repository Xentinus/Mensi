import type { DailyLog } from '~/types/api'
import { CRAMP_SEVERITY_LABELS, CRAMP_TYPE_LABELS, FLOW_LABELS, LH_LABELS, MOOD_EMOJI, MOOD_LABELS, MUCUS_LABELS, type FieldKey } from '~/utils/labels'
import { formatTemp } from '~/utils/format'

/** A napló egy mezőjének kijelzett értéke; null = nincs rögzítve.
 *  Megjegyzés: az együttlét "explicit 0" állapotot a séma nem tárolja —
 *  üres eseménylista = nincs rögzítve. */
export function fieldValue(log: DailyLog | null, key: FieldKey): string | null {
  if (!log) return null
  switch (key) {
    case 'bbt':
      return formatTemp(log.bbtCelsius)
    case 'mucus':
      return log.cervicalMucus ? MUCUS_LABELS[log.cervicalMucus] : null
    case 'lh':
      return log.lhTest ? LH_LABELS[log.lhTest] : null
    case 'cramp':
      if (log.crampSeverity === null) return null
      if (log.crampSeverity === 0) return 'Nincs'
      return `${log.crampType ? CRAMP_TYPE_LABELS[log.crampType] + ' · ' : ''}${CRAMP_SEVERITY_LABELS[log.crampSeverity]}`
    case 'flow': {
      if (log.flowIntensity === null) return log.periodStart ? 'Ciklus 1. napja' : null
      const base = FLOW_LABELS[log.flowIntensity]
      return log.periodStart ? `${base} · ciklus 1. napja` : base
    }
    case 'intercourse': {
      if (log.intercourse.length === 0) return null
      const prot = log.intercourse.filter(e => e.protected === true).length
      return `${log.intercourse.length}×${prot > 0 ? ` · ${prot} védekezéssel` : ''}`
    }
    case 'mood':
      return log.moods.length
        ? log.moods.map(m => `${MOOD_EMOJI[m]} ${MOOD_LABELS[m]}`).join(', ')
        : null
  }
}
