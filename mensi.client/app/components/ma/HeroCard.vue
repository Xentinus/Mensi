<script setup lang="ts">
import type { Overview, StripDay } from '~/types/api'
import { CATEGORY_LEGEND, CONFIDENCE_LABELS, STRIP_COLORS } from '~/utils/labels'
import { formatDateShort, formatRange } from '~/utils/format'

const props = defineProps<{ overview: Overview }>()
const o = computed(() => props.overview)
const WEEK_HEADS = ['H', 'K', 'Sz', 'Cs', 'P', 'Szo', 'V']

function tagOf(day: StripDay): string {
  if (day.isToday) return 'ma'
  const d = new Date(`${day.date}T00:00:00`)
  return d.getDate() === 1 ? formatDateShort(day.date).split(' ')[0]! : ''
}
const dayNum = (day: StripDay) => new Date(`${day.date}T00:00:00`).getDate()
</script>

<template>
  <div class="hero">
    <div class="hero-top">
      <div class="hero-row">
        <span class="hero-tag">Ciklus {{ o.cycle!.day }}. nap</span>
        <span class="hero-since">{{ formatDateShort(o.cycle!.startDate) }} óta</span>
      </div>
      <div class="hero-headline">{{ o.headline }}</div>
      <div class="hero-boxes">
        <div class="hero-box">
          <div class="hero-box-label">Ovuláció</div>
          <div class="hero-box-value">{{ formatRange(o.ovulationWindow!.from, o.ovulationWindow!.to) }}</div>
        </div>
        <div class="hero-box">
          <div class="hero-box-label">Következő menstruáció</div>
          <div class="hero-box-value">{{ formatRange(o.nextPeriodWindow!.from, o.nextPeriodWindow!.to) }}</div>
        </div>
      </div>
      <div class="phase">
        <div class="phase-row">
          <span class="phase-label">{{ o.phase!.label }}</span>
          <span class="phase-remaining">{{ o.phase!.remainingDays }} nap</span>
        </div>
        <div class="phase-dots">
          <div v-for="i in o.phase!.totalDays" :key="i" class="phase-dot"
            :class="{ done: i <= o.phase!.elapsedDays }" />
        </div>
      </div>
      <div v-if="o.measurementHint" class="measure-hint">{{ o.measurementHint }}</div>
    </div>

    <div class="hero-bottom">
      <div class="strip-head">
        <span class="strip-range">{{ formatRange(o.strip!.from, o.strip!.to) }}</span>
        <span class="chip strip-conf">konfidencia: {{ CONFIDENCE_LABELS[o.confidence!] }}</span>
      </div>
      <div class="strip-grid">
        <div v-for="w in WEEK_HEADS" :key="w" class="strip-weekhead">{{ w }}</div>
        <div v-for="day in o.strip!.days" :key="day.date" class="strip-cell" :style="{
          background: STRIP_COLORS[day.category].bg,
          color: STRIP_COLORS[day.category].fg,
          boxShadow: day.isToday ? 'inset 0 0 0 2px #21243d' : 'none',
        }">
          <span class="strip-tag">{{ tagOf(day) }}</span>
          <span class="strip-num" :class="{ bold: day.isToday || day.category === 'ovulation' }">
            {{ dayNum(day) }}
          </span>
        </div>
      </div>
      <div class="legend">
        <div v-for="item in CATEGORY_LEGEND" :key="item.key" class="legend-item">
          <span class="legend-dot" :style="{ background: STRIP_COLORS[item.key].bg }" />
          <span>{{ item.label }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.hero { border-radius: 20px; overflow: hidden; box-shadow: 0 1px 2px rgba(33,36,61,.05), 0 6px 22px rgba(33,36,61,.07); }
.hero-top { background: var(--primary); padding: 20px 18px 18px; }
.hero-row { display: flex; align-items: center; }
.hero-tag { font-size: 12px; font-weight: 600; color: #d2daff; }
.hero-since { margin-left: auto; font-size: 11.5px; font-weight: 500; color: var(--lavender); }
.hero-headline { font-size: 25px; font-weight: 700; color: #fff; letter-spacing: -.025em; line-height: 1.18; margin-top: 10px; }
.hero-boxes { display: flex; gap: 8px; margin-top: 16px; }
.hero-box { flex: 1; background: rgba(255,255,255,.16); border-radius: 12px; padding: 11px 13px; }
.hero-box-label { font-size: 10.5px; font-weight: 600; color: #d2daff; }
.hero-box-value { font-size: 15px; font-weight: 700; color: #fff; margin-top: 3px; }
.phase { margin-top: 14px; }
.phase-row { display: flex; align-items: baseline; }
.phase-label { font-size: 11px; font-weight: 600; color: #d2daff; }
.phase-remaining { margin-left: auto; font-size: 11px; font-weight: 700; color: #fff; }
.phase-dots { display: flex; gap: 4px; margin-top: 7px; }
.phase-dot { flex: 1; height: 7px; border-radius: 99px; background: #fff; }
.phase-dot.done { background: rgba(255,255,255,.35); }
.hero-bottom { background: #fff; padding: 18px; }
.measure-hint {
  margin-top: 14px; background: rgba(255, 255, 255, .14); border-radius: 14px;
  padding: 13px 14px; font-size: 12.5px; color: #eceeff; line-height: 1.55;
}
.strip-head { display: flex; align-items: center; }
.strip-range { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.strip-conf { margin-left: auto; color: var(--primary-hover); background: var(--tint); font-weight: 700; }
.strip-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 5px; margin: 14px auto 0; max-width: 400px; }
.strip-weekhead { text-align: center; font-size: 10px; font-weight: 600; color: var(--ink-4); padding-bottom: 2px; }
.strip-cell {
  aspect-ratio: 1; border-radius: 10px; display: flex; flex-direction: column;
  align-items: center; justify-content: center; gap: 1px;
}
.strip-tag { font-size: 8px; font-weight: 600; line-height: 1; min-height: 8px; }
.strip-num { font-size: 11.5px; font-weight: 500; line-height: 1.1; }
.strip-num.bold { font-weight: 700; }
.legend { display: flex; flex-wrap: wrap; gap: 9px 13px; margin-top: 15px; }
.legend-item { display: flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 500; color: var(--ink-2); }
.legend-dot { width: 9px; height: 9px; border-radius: 3px; }
</style>
