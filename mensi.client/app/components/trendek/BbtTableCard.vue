<script setup lang="ts">
import type { Trends } from '~/types/api'
import { LH_LABELS, MUCUS_LABELS } from '~/utils/labels'
import { formatDateShort, formatDelta, formatTemp } from '~/utils/format'

const props = defineProps<{ bbt: NonNullable<Trends['bbt']> }>()
const comma2 = (n: number) => n.toFixed(2).replace('.', ',')

function marks(row: NonNullable<Trends['bbt']>['rows'][number]): string {
  if (row.isOutlier) return 'kiugró'
  const parts: string[] = []
  if (row.marks.cervicalMucus) parts.push(MUCUS_LABELS[row.marks.cervicalMucus].toLowerCase())
  if (row.marks.lhValue !== null) parts.push(`LH ${row.marks.lhValue.toFixed(2).replace('.', ',')}`)
  return parts.join(' · ') || '—'
}
</script>

<template>
  <div class="card">
    <div class="head">
      <span class="section-title">Bazális testhő</span>
      <span v-if="bbt.coverline !== null" class="chip cover">Coverline {{ comma2(bbt.coverline) }} °C</span>
    </div>

    <div class="thead">
      <span class="c-day">Nap</span><span class="c-date">Dátum</span><span class="c-temp">Mérés</span>
      <span class="c-delta">Eltérés</span><span class="c-marks">Jelek</span>
    </div>
    <div v-for="row in bbt.rows" :key="row.date" class="trow" :class="{ above: row.aboveCoverline }">
      <span class="c-day day" :class="{ dim: row.value === null }">{{ row.cycleDay }}</span>
      <span class="c-date date">{{ formatDateShort(row.date) }}</span>
      <span class="c-temp temp" :class="{ dim: row.value === null, above: row.aboveCoverline }">
        {{ row.value === null ? 'nincs mérés' : formatTemp(row.value) }}</span>
      <span class="c-delta delta" :class="{ pos: (row.deltaFromCoverline ?? -1) >= 0 }">
        {{ row.deltaFromCoverline === null ? '—' : formatDelta(row.deltaFromCoverline) }}</span>
      <span class="c-marks marks">{{ marks(row) }}</span>
    </div>

    <div class="flags">
      <span v-if="bbt.excludedOutlierCount > 0" class="chip flag-a">
        {{ bbt.excludedOutlierCount }} kiugró érték kihagyva a coverline-ból</span>
      <span v-if="bbt.missingDayCount > 0" class="chip flag-b">{{ bbt.missingDayCount }} nap kimaradt</span>
    </div>
    <div class="status">
      <template v-if="bbt.ovulationConfirmed">
        Az ovuláció hőemelkedéssel <b>megerősítve</b> — {{ formatDateShort(bbt.confirmedOvulationDate!) }}
      </template>
      <template v-else>
        Az ovuláció hőemelkedéssel <b>még nem erősödött meg</b> — ehhez három egymást követő
        magasabb érték kell a coverline fölött.
      </template>
    </div>
  </div>
</template>

<style scoped>
.head { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.cover { margin-left: auto; color: var(--primary-hover); background: var(--tint); }
.thead { display: flex; padding: 16px 0 9px; font-size: 10.5px; font-weight: 600; color: var(--ink-3); }
.trow { display: flex; align-items: center; padding: 9px 8px; margin: 0 -8px; border-radius: 10px; border-top: 1px solid var(--line); }
.trow.above { background: var(--tint); }
.c-day { width: 30px; flex-shrink: 0; }
.c-date { width: 58px; flex-shrink: 0; }
.c-temp { width: 84px; flex-shrink: 0; }
.c-delta { width: 56px; flex-shrink: 0; text-align: right; }
.c-marks { flex: 1; text-align: right; }
.day { font-size: 12.5px; font-weight: 700; }
.day.dim { color: var(--muted); }
.date { font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.temp { font-size: 13.5px; font-weight: 600; white-space: nowrap; }
.temp.dim { font-weight: 500; color: var(--muted); }
.temp.above { font-weight: 700; color: var(--primary-hover); }
.delta { font-size: 12px; font-weight: 600; color: var(--ink-4); }
.delta.pos { color: var(--primary-hover); }
.marks { font-size: 10.5px; font-weight: 600; color: var(--primary-hover); }
.flags { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 16px; }
.flag-a { font-size: 11.5px; color: var(--primary-hover); background: var(--tint); padding: 7px 12px; }
.flag-b { font-size: 11.5px; color: var(--ink-2); background: #e3e8fb; padding: 7px 12px; }
.status { margin-top: 12px; background: var(--tint); border-radius: 14px; padding: 13px 14px; font-size: 12.5px; color: var(--primary-ink); line-height: 1.55; }
</style>
