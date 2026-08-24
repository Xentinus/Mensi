import { describe, expect, it } from 'vitest'
import type { DailyLog, LogPatch } from '~/types/api'
import { buildInversePatch, mergedUndoPayload } from '~/utils/undo'

const before: DailyLog = {
  date: '2026-08-20',
  bbtCelsius: 36.1,
  bbtOutlier: false,
  cervicalMucus: 'dry',
  lhTest: 'negative',
  crampType: 'abdomen',
  crampSeverity: 2,
  flowIntensity: null,
  periodStart: false,
  moods: ['calm', 'tired'],
  intercourse: [{ id: 1, protected: false }],
  updatedAt: '2026-08-20T10:00:00Z',
  updatedBy: 'someone',
}

describe('buildInversePatch', () => {
  it('csak a patch-ben szereplő kulcsokhoz épít inverzet, a mentés ELŐTTI értékekkel', () => {
    const patch: LogPatch = { bbtCelsius: 37, moods: ['cheerful'] }
    expect(buildInversePatch(patch, before)).toEqual({ bbtCelsius: 36.1, moods: ['calm', 'tired'] })
  })

  it('before === null esetén minden érintett kulcs inverze null, periodStart pedig false', () => {
    const patch: LogPatch = { cervicalMucus: 'eggWhite', periodStart: true }
    expect(buildInversePatch(patch, null)).toEqual({ cervicalMucus: null, periodStart: false })
  })

  it('periodStart inverze mindig boolean, sosem null, ha a patch tartalmazza', () => {
    expect(buildInversePatch({ periodStart: true }, before)).toEqual({ periodStart: false })
  })
})

describe('mergedUndoPayload', () => {
  it('mező-patch ÉS együttlét egy körben mentve — EGY payload, mindkét ág megvan egyszerre (a kritikus regresszió)', () => {
    // Ez a Finding 1 direkt regressziós tesztje: korábban a mező-patch és az együttlét mentése
    // két külön showToast-hívást indított, és a második payload ({ patch: null, events: [...] })
    // felülírta az elsőt, így a Visszavonás csak az együttlétet állította vissza, a mező-változások
    // véglegesen mentve maradtak. Az összevont payloadban mindkét ágnak jelen kell lennie.
    const patch: LogPatch = { cervicalMucus: 'eggWhite' }
    const events = [{ protected: true }, { protected: false }]
    const payload = mergedUndoPayload('2026-08-20', patch, events, before)
    expect(payload.patch).toEqual({ cervicalMucus: 'dry' })
    expect(payload.events).toEqual([{ protected: false }])
  })

  it('csak mező-patch mentésekor events: null', () => {
    const payload = mergedUndoPayload('2026-08-20', { lhTest: 'positive' }, null, before)
    expect(payload).toEqual({ date: '2026-08-20', patch: { lhTest: 'negative' }, events: null })
  })

  it('csak együttlét mentésekor patch: null', () => {
    const payload = mergedUndoPayload('2026-08-20', null, [{ protected: true }], before)
    expect(payload).toEqual({ date: '2026-08-20', patch: null, events: [{ protected: false }] })
  })

  it('before === null esetén az events-inverz üres tömb, nem null', () => {
    const payload = mergedUndoPayload('2026-08-20', null, [{ protected: true }], null)
    expect(payload.events).toEqual([])
  })
})
