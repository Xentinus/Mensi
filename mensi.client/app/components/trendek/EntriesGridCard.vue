<script setup lang="ts">
import type { DailyLog, Trends } from '~/types/api'
import { MOOD_EMOJI } from '~/utils/labels'

const props = defineProps<{ rows: NonNullable<Trends['bbt']>['rows']; logs: DailyLog[] }>()

const MUCUS_RAMP = ['#f2f6ff', '#dfe9ff', '#c6d6ff', '#aac4ff']
const CRAMP_RAMP = ['#f3f3ff', '#e4e4ff', '#cfd0ff', '#b1b2ff']
const FLOW_RAMP = ['#eeeefb', '#dcddf6', '#c5c6f0', '#adaee9', '#9698e2']
const MUCUS_IDX = { dry: 0, sticky: 1, creamy: 2, eggWhite: 3 } as const
const FLOW_IDX = { none: 0, spotting: 1, light: 2, medium: 3, heavy: 4 } as const

interface Cell { bg: string; fg: string; txt: string }
const OFF: Cell = { bg: 'var(--bg)', fg: 'transparent', txt: '' }

const byDate = computed(() => new Map(props.logs.map(l => [l.date, l])))
const gridRows = computed(() => {
  const defs: { label: string; cell: (log: DailyLog | undefined) => Cell }[] = [
    { label: 'Testhő', cell: l => l?.bbtCelsius != null ? { bg: '#dde1ef', fg: 'transparent', txt: '' } : OFF },
    { label: 'Nyák', cell: l => l?.cervicalMucus ? { bg: MUCUS_RAMP[MUCUS_IDX[l.cervicalMucus]]!, fg: '#1e3566', txt: String(MUCUS_IDX[l.cervicalMucus] + 1) } : OFF },
    // Az LH-arány folytonos: a cella háttere a csík sötétségét követi, a szám a 0–1 érték
    // tizedei — így a 0,10 és a 0,40 nap ránézésre is elválik.
    { label: 'LH', cell: (l) => {
      if (l?.lhValue == null) return OFF
      const strong = l.lhValue >= 0.5
      return {
        bg: `rgba(90,92,214,${(0.12 + l.lhValue * 0.85).toFixed(2)})`,
        fg: strong ? '#fff' : '#2c2d63',
        txt: String(Math.round(l.lhValue * 10)),
      }
    } },
    { label: 'Görcs', cell: l => l?.crampSeverity != null && l.crampSeverity > 0 ? { bg: CRAMP_RAMP[l.crampSeverity]!, fg: '#2c2d63', txt: String(l.crampSeverity) } : OFF },
    { label: 'Folyás', cell: l => l?.flowIntensity && l.flowIntensity !== 'none' ? { bg: FLOW_RAMP[FLOW_IDX[l.flowIntensity]]!, fg: '#26265c', txt: String(FLOW_IDX[l.flowIntensity]) } : OFF },
    { label: 'Együttlét', cell: l => l && l.intercourse.length > 0 ? { bg: '#5a5cd6', fg: '#fff', txt: String(l.intercourse.length) } : OFF },
    { label: 'Hangulat', cell: l => l && l.moods.length > 0 ? { bg: 'rgba(90,92,214,.16)', fg: '#3a3c9e', txt: MOOD_EMOJI[l.moods[0]!] } : OFF },
  ]
  return defs.map(def => ({
    label: def.label,
    cells: props.rows.map(r => def.cell(byDate.value.get(r.date))),
  }))
})
</script>

<template>
  <div class="card grid-card">
    <div class="head">
      <span class="section-title">Bejegyzések</span>
      <span class="hint">görgethető →</span>
    </div>
    <div class="scroll noscroll">
      <div class="grid" :style="{ minWidth: `${80 + rows.length * 24}px` }">
        <div class="grid-days">
          <div v-for="r in rows" :key="r.date" class="grid-day"
            :class="{ today: r.cycleDay === rows.length }">{{ r.cycleDay }}</div>
        </div>
        <div v-for="row in gridRows" :key="row.label" class="grid-row">
          <div class="grid-label">{{ row.label }}</div>
          <div class="grid-cells">
            <div v-for="(cell, i) in row.cells" :key="i" class="grid-cell"
              :style="{ background: cell.bg, color: cell.fg }">{{ cell.txt }}</div>
          </div>
        </div>
      </div>
    </div>
    <div class="footnote">Halvány cella = aznap nem volt bejegyzés. A telítettség az intenzitást jelöli.</div>
  </div>
</template>

<style scoped>
.grid-card { padding-left: 0; padding-right: 0; }
.head { padding: 0 18px; display: flex; align-items: baseline; }
.hint { margin-left: auto; font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.scroll { overflow-x: auto; margin-top: 14px; padding: 0 18px; }
.grid-days { display: flex; gap: 2px; padding-left: 80px; margin-bottom: 6px; }
.grid-day { flex: 1; min-width: 20px; text-align: center; font-size: 9px; font-weight: 600; color: var(--ink-4); }
.grid-day.today { color: var(--primary); }
.grid-row { display: flex; align-items: center; margin-bottom: 4px; }
.grid-label { width: 80px; flex-shrink: 0; font-size: 11px; font-weight: 600; color: var(--ink-2); }
.grid-cells { flex: 1; display: flex; gap: 2px; }
.grid-cell {
  flex: 1; min-width: 20px; height: 20px; border-radius: 6px; display: grid; place-items: center;
  font-size: 8.5px; font-weight: 700;
}
.footnote { padding: 14px 18px 0; font-size: 11.5px; color: var(--ink-3); line-height: 1.5; }
</style>
