<script setup lang="ts">
import type { Chance } from '~/types/api'
import { TIMING_LABELS } from '~/utils/labels'
import { formatDateShort, formatPercent } from '~/utils/format'

const store = useAppStore()
const api = useApi()
const chance = ref<Chance | null>(null)
watch(() => store.refreshTick, async () => { chance.value = await api.chance() }, { immediate: true })

const METHOD_NOTES = [
  'Az ovulációs ablak a lezárt ciklusok hosszából és luteális fázisából jön, az LH-teszttel és a nyákkal korrigálva.',
  'A százalék a Wilcox-féle, ovuláció-relatív napi valószínűségekből számított becslés — nem orvosi termékenységi vizsgálat.',
  'A hiányzó napokat nem pótolja becsléssel: ahol nincs adat, ott „nincs bejegyzés" szerepel.',
  'Nem veszi figyelembe az életkort, spermaminőséget, gyógyszereket és semmilyen orvosi tényezőt.',
]
const short = (iso: string) => formatDateShort(iso).replace(' ', '')
const barStyle = (label: 'weak' | 'medium' | 'good') => ({
  width: label === 'good' ? '100%' : label === 'medium' ? '62%' : '30%',
  background: label === 'good' ? '#2f3170' : label === 'medium' ? 'var(--primary)' : '#a8adc7',
})
</script>

<template>
  <div v-if="chance" class="stack">
    <div v-if="chance.isEmpty" class="card empty">Az esély-számításhoz legalább egy lezárt ciklus kell.</div>
    <template v-else>
      <div class="card">
        <div class="label">Időzítés ebben a ciklusban</div>
        <div class="big">{{ TIMING_LABELS[chance.timing!.label] }}</div>
        <div class="percent">becsült esély ebben a ciklusban: <b>{{ formatPercent(chance.timing!.chancePercent) }}</b></div>
        <div class="body">{{ chance.explanation }}</div>
        <div class="note">{{ chance.confidenceNote }}</div>
      </div>

      <div class="card">
        <div class="section-title">Termékeny ablak napjai</div>
        <div class="days">
          <div v-for="d in chance.fertileWindow!.days" :key="d.date" class="day-col">
            <div class="day-box" :style="{
              background: d.intercourseCount > 0 ? 'var(--primary)' : d.isFuture ? '#f5f7fe' : '#e3e8fb',
              color: d.intercourseCount > 0 ? '#fff' : d.isFuture ? '#b8bedb' : '#3f4f9c',
              boxShadow: d.isToday ? 'inset 0 0 0 2px #21243d' : 'none',
            }">
              <span class="day-num">{{ d.cycleDay }}</span>
              <span v-if="d.intercourseCount > 0" class="day-count">{{ d.intercourseCount }}×</span>
            </div>
            <span class="day-date">{{ short(d.date) }}</span>
          </div>
        </div>
        <div class="legend">
          <span class="legend-item"><span class="dot" style="background:#5a5cd6" />Volt együttlét</span>
          <span class="legend-item"><span class="dot" style="background:#e3e8fb" />Nincs bejegyzés</span>
          <span class="legend-item"><span class="dot" style="background:#f5f7fe" />Még hátra van</span>
        </div>
      </div>

      <div class="cols">
        <div class="remaining">
          <div class="rem-title">A hátralévő ablak</div>
          <div class="rem-big">
            <span class="rem-num">{{ chance.fertileWindow!.daysRemaining }}</span>
            <span class="rem-unit">nap van hátra</span>
          </div>
          <div class="rem-dots">
            <div v-for="i in chance.fertileWindow!.ovulationWindowTotal" :key="i" class="rem-dot"
              :class="{ done: i <= chance.fertileWindow!.ovulationWindowElapsed }" />
          </div>
          <div v-if="chance.whatIfHint" class="rem-hint">{{ chance.whatIfHint }}</div>
        </div>

        <div class="card">
          <div class="hist-head">
            <span class="section-title">Korábbi ciklusok</span>
            <span class="chip hist-chip">{{ chance.history!.goodCount }} jó a {{ chance.history!.totalCount }}-ból</span>
          </div>
          <div class="hist-rows">
            <div v-for="c in chance.history!.cycles" :key="c.startDate" class="hist-row">
              <span class="hist-label">{{ formatDateShort(c.startDate).split(' ')[0] }}</span>
              <div class="hist-track"><div class="hist-bar" :style="barStyle(c.timing.label)" /></div>
              <span class="hist-timing" :style="{ color: barStyle(c.timing.label).background }">
                {{ TIMING_LABELS[c.timing.label] }}</span>
            </div>
          </div>
        </div>
      </div>

      <div class="method">
        <div class="method-title">Módszertan</div>
        <div class="method-notes">
          <div v-for="(m, i) in METHOD_NOTES" :key="i" class="method-note">
            <span class="method-dot" /><span>{{ m }}</span>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.empty { font-size: 13px; color: var(--ink-2); }
.label { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.big { font-size: 36px; font-weight: 700; margin-top: 6px; letter-spacing: -.03em; color: var(--primary-hover); }
.percent { font-size: 13.5px; color: var(--ink); margin-top: 4px; }
.body { font-size: 13.5px; color: var(--ink-2); line-height: 1.6; margin-top: 9px; }
.note { font-size: 11.5px; color: var(--muted); line-height: 1.5; margin-top: 9px; }
.days { display: flex; gap: 6px; margin-top: 16px; }
.day-col { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 6px; }
.day-box {
  width: 100%; aspect-ratio: 1; border-radius: 12px; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 2px;
}
.day-num { font-size: 13px; font-weight: 700; }
.day-count { font-size: 9px; font-weight: 700; }
.day-date { font-size: 8.5px; font-weight: 600; color: var(--muted); }
.legend { display: flex; flex-wrap: wrap; gap: 10px; margin-top: 16px; }
.legend-item { display: flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 500; color: var(--ink-2); }
.dot { width: 9px; height: 9px; border-radius: 3px; display: inline-block; }
.cols { display: flex; flex-direction: column; gap: 14px; }
@media (min-width: 700px) { .cols { display: grid; grid-template-columns: 1fr 1fr; } }
.remaining { background: var(--primary); border-radius: 20px; padding: 20px 18px; box-shadow: 0 6px 22px rgba(90,92,214,.22); }
.rem-title { font-size: 13px; font-weight: 700; color: #fff; }
.rem-big { display: flex; align-items: baseline; gap: 8px; margin-top: 10px; }
.rem-num { font-size: 34px; font-weight: 700; color: #fff; letter-spacing: -.03em; }
.rem-unit { font-size: 14px; font-weight: 600; color: #d2daff; }
.rem-dots { display: flex; gap: 4px; margin-top: 14px; }
.rem-dot { flex: 1; height: 8px; border-radius: 99px; background: #fff; }
.rem-dot.done { background: rgba(255,255,255,.35); }
.rem-hint { margin-top: 14px; background: rgba(255,255,255,.14); border-radius: 14px; padding: 13px 14px; font-size: 12.5px; color: #eceeff; line-height: 1.55; }
.hist-head { display: flex; align-items: baseline; }
.hist-chip { margin-left: auto; color: var(--primary-hover); background: var(--tint); font-size: 11px; font-weight: 600; }
.hist-rows { display: flex; flex-direction: column; gap: 8px; margin-top: 14px; }
.hist-row { display: flex; align-items: center; gap: 10px; }
.hist-label { width: 44px; flex-shrink: 0; font-size: 11.5px; font-weight: 600; color: var(--ink-3); }
.hist-track { flex: 1; height: 8px; border-radius: 99px; background: var(--tint); overflow: hidden; }
.hist-bar { height: 100%; border-radius: 99px; }
.hist-timing { width: 52px; flex-shrink: 0; text-align: right; font-size: 11px; font-weight: 700; }
.method { background: var(--tint); border-radius: 20px; padding: 20px 18px; }
.method-title { font-size: 13px; font-weight: 700; color: var(--primary); }
.method-notes { display: flex; flex-direction: column; gap: 10px; margin-top: 12px; }
.method-note { display: flex; gap: 10px; align-items: flex-start; font-size: 12.5px; color: var(--primary-ink); line-height: 1.55; }
.method-dot { width: 6px; height: 6px; border-radius: 99px; background: var(--primary); margin-top: 7px; flex-shrink: 0; }
</style>
