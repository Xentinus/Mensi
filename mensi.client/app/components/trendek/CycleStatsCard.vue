<script setup lang="ts">
import type { Trends } from '~/types/api'
import { TIMING_LABELS } from '~/utils/labels'
import { formatDateShort } from '~/utils/format'

const props = defineProps<{ trends: Trends }>()
const s = computed(() => props.trends.stats!)
const cycles = computed(() => props.trends.cycles)

// A sáv-vizualizáció skálája: [min−2, max+2] napra feszítve.
const axisMin = computed(() => s.value.minLength - 2)
const axisMax = computed(() => s.value.maxLength + 2)
const pos = (n: number) => `${((n - axisMin.value) / (axisMax.value - axisMin.value)) * 100}%`
const axis = computed(() => {
  const ticks: number[] = []
  for (let n = axisMin.value; n <= axisMax.value; n += 2) ticks.push(n)
  return ticks
})
const comma1 = (n: number) => n.toFixed(1).replace('.', ',')

const timingStyle = (label: 'weak' | 'medium' | 'good') => ({
  color: label === 'good' ? '#2f3170' : label === 'medium' ? '#4a4cbd' : '#626884',
  background: label === 'good' ? '#d2daff' : label === 'medium' ? '#eef1ff' : '#eef0f7',
})
const dev = (n: number) => n === 0 ? 'átlagos' : `${n > 0 ? '+' : '−'}${Math.abs(n)} nap`
</script>

<template>
  <div class="card">
    <div class="label">Ciklushossz · utolsó {{ Math.min(cycles.length, 6) }} ciklus</div>
    <div class="big-row">
      <span class="big">{{ Math.round(s.averageLength) }}</span>
      <span class="big-unit">nap az átlag</span>
    </div>
    <div class="sentence">A ciklusaid <b>{{ s.minLength }} és {{ s.maxLength }} nap</b> között mozogtak,
      ±{{ comma1(s.stdDev) }} nap szórással.</div>

    <div class="band">
      <div class="band-track" />
      <div class="band-range" :style="{ left: pos(s.minLength), width: `calc(${pos(s.maxLength)} - ${pos(s.minLength)})` }" />
      <div class="band-avg" :style="{ left: pos(s.averageLength) }" />
      <div v-for="c in cycles.slice(0, 6)" :key="c.startDate" class="band-mark"
        :style="{ left: pos(c.lengthDays), background: Math.abs(c.deviationFromAverage) >= 2 ? 'var(--primary)' : 'var(--lavender)' }" />
      <div v-for="t in axis" :key="t" class="band-tick" :style="{ left: pos(t) }">{{ t }}</div>
      <div class="band-avg-label" :style="{ left: pos(s.averageLength) }">átlag {{ Math.round(s.averageLength) }}</div>
    </div>

    <div class="tiles">
      <div class="tile"><div class="tile-v">{{ s.minLength }}</div><div class="tile-l">legrövidebb</div></div>
      <div class="tile"><div class="tile-v">{{ s.maxLength }}</div><div class="tile-l">leghosszabb</div></div>
      <div class="tile accent"><div class="tile-v">{{ s.averageLuteal === null ? '—' : comma1(s.averageLuteal) }}</div><div class="tile-l">luteális</div></div>
      <div class="tile"><div class="tile-v">{{ s.loggedPercent }}%</div><div class="tile-l">rögzített</div></div>
    </div>

    <div class="table-head">
      <span class="col-start">Kezdet</span><span class="col-len">Hossz</span>
      <span class="col-dev">Átlaghoz</span><span class="col-lut">Luteális</span>
      <span class="col-tim">Időzítés</span>
    </div>
    <div v-for="c in cycles" :key="c.startDate" class="table-row"
      :class="{ hot: Math.abs(c.deviationFromAverage) >= 2 }">
      <span class="col-start row-start">{{ formatDateShort(c.startDate) }}</span>
      <span class="col-len row-len">{{ c.lengthDays }} nap</span>
      <span class="col-dev"><span class="chip" :style="timingStyle(Math.abs(c.deviationFromAverage) >= 2 ? 'good' : 'medium')">{{ dev(c.deviationFromAverage) }}</span></span>
      <span class="col-lut row-lut">{{ c.anovulatory ? 'anovul.' : c.lutealLength === null ? '—' : `${c.lutealLength} nap` }}</span>
      <span class="col-tim"><span class="chip" :style="timingStyle(c.timing.label)">{{ TIMING_LABELS[c.timing.label] }}</span></span>
    </div>
  </div>
</template>

<style scoped>
.label { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.big-row { display: flex; align-items: baseline; gap: 9px; margin-top: 7px; }
.big { font-size: 38px; font-weight: 700; letter-spacing: -.035em; line-height: 1; }
.big-unit { font-size: 14px; font-weight: 600; color: var(--ink-2); }
.sentence { font-size: 13px; color: var(--ink-2); line-height: 1.55; margin-top: 8px; }
.band { position: relative; margin-top: 28px; height: 58px; }
.band-track { position: absolute; left: 0; right: 0; top: 22px; height: 10px; border-radius: 99px; background: #f0f2fb; }
.band-range { position: absolute; top: 22px; height: 10px; border-radius: 99px; background: var(--light-blue); }
.band-avg { position: absolute; top: 14px; width: 3px; height: 26px; margin-left: -1.5px; border-radius: 99px; background: var(--primary); }
.band-mark { position: absolute; top: 19px; width: 16px; height: 16px; margin-left: -8px; border-radius: 99px; border: 2px solid #fff; box-shadow: 0 1px 3px rgba(33,36,61,.2); }
.band-tick { position: absolute; top: 40px; transform: translateX(-50%); font-size: 10px; font-weight: 600; color: var(--ink-4); }
.band-avg-label { position: absolute; top: -4px; transform: translateX(-50%); font-size: 10px; font-weight: 700; color: var(--primary); white-space: nowrap; }
.tiles { display: flex; gap: 8px; margin-top: 22px; flex-wrap: wrap; }
.tile { flex: 1; min-width: 78px; background: var(--surface); border-radius: 14px; padding: 13px 12px; text-align: center; }
.tile.accent { background: var(--tint); }
.tile-v { font-size: 18px; font-weight: 700; letter-spacing: -.02em; }
.tile.accent .tile-v { color: #2f3170; }
.tile-l { font-size: 10.5px; font-weight: 600; color: var(--ink-3); margin-top: 2px; }
.table-head { display: flex; padding: 22px 0 9px; border-top: 1px solid var(--line); margin-top: 20px; font-size: 10.5px; font-weight: 600; color: var(--ink-3); }
.table-row { display: flex; align-items: center; padding: 11px 8px; margin: 0 -8px; border-radius: 10px; border-top: 1px solid var(--line); }
.table-row.hot { background: var(--surface); }
.col-start { flex: 1.4; }
.col-len { width: 58px; flex-shrink: 0; text-align: right; }
.col-dev { width: 78px; flex-shrink: 0; text-align: right; }
.col-lut { width: 62px; flex-shrink: 0; text-align: right; }
.col-tim { width: 72px; flex-shrink: 0; text-align: right; }
.row-start { font-size: 13px; font-weight: 600; }
.row-len { font-size: 13.5px; font-weight: 700; }
.row-lut { font-size: 12.5px; font-weight: 500; color: var(--ink-2); }
.chip { font-size: 11px; font-weight: 700; padding: 4px 9px; white-space: nowrap; }
</style>
