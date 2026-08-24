import type { DailyLog, LogPatch } from '~/types/api'

export interface UndoPayload {
  date: string
  patch: LogPatch | null
  events: { protected: boolean | null }[] | null
}

/** Egy elmentett patch inverze — a patch ÉRINTETT kulcsaihoz a mentés ELŐTTI értékek,
 *  hogy a Visszavonás pontosan azokat a mezőket állítsa vissza, amiket a mentés módosított. */
export function buildInversePatch(patch: LogPatch, before: DailyLog | null): LogPatch {
  const inverse: LogPatch = {}
  for (const key of Object.keys(patch) as (keyof LogPatch)[]) {
    // @ts-expect-error kulcsonként azonos típus a két oldalon
    inverse[key] = before ? (before[key === 'moods' ? 'moods' : key] ?? null) : null
  }
  if ('periodStart' in patch) inverse.periodStart = before?.periodStart ?? false
  return inverse
}

/** Egy mentési kör (mező-patch és/vagy együttlét) EGYETLEN undo-payloadja.
 *  Ha egy körben mindkét ág mentésre kerül, ez akadályozza meg, hogy a második
 *  showToast felülírja az elsőt — anélkül csak a második változás lenne visszavonható. */
export function mergedUndoPayload(
  date: string,
  patch: LogPatch | null,
  events: { protected: boolean | null }[] | null,
  before: DailyLog | null,
): UndoPayload {
  return {
    date,
    patch: patch ? buildInversePatch(patch, before) : null,
    events: events ? (before?.intercourse ?? []).map(e => ({ protected: e.protected })) : null,
  }
}
