<script setup lang="ts">
import type { Overview } from '~/types/api'
import { TIMING_LABELS } from '~/utils/labels'
import { formatDateShort, formatPercent } from '~/utils/format'

const props = defineProps<{ overview: Overview }>()
const t = computed(() => props.overview.timing!)
const short = (iso: string) => formatDateShort(iso).replace(' ', '')
</script>

<template>
  <NuxtLink to="/esely" class="card timing">
    <div class="head">
      <span class="head-label">Időzítés ebben a ciklusban</span>
      <span class="chip head-chip">{{ TIMING_LABELS[t.label] }} · {{ formatPercent(t.chancePercent) }}</span>
    </div>
    <div class="days">
      <div v-for="d in t.windowDays" :key="d.date" class="day" :style="{
        background: d.intercourseCount > 0 ? 'var(--primary)' : d.isFuture ? 'var(--surface)' : '#e3e8fb',
        boxShadow: d.isOvulationWindow ? 'inset 0 0 0 2px var(--primary)' : 'none',
      }">
        <span v-if="d.intercourseCount > 0" class="day-count">{{ d.intercourseCount }}×</span>
      </div>
    </div>
    <div class="dates">
      <div v-for="d in t.windowDays" :key="d.date" class="date"
        :class="{ ovu: d.isOvulationWindow }">{{ short(d.date) }}</div>
    </div>
    <div class="note">
      <span class="note-mark" />
      <span>Ovulációs ablak — itt számít legtöbbet az együttlét</span>
    </div>
    <div class="summary">
      <span>{{ t.intercourseTotal }} együttlét · {{ t.daysRemaining }} nap hátra</span>
      <span class="details">Részletek</span>
    </div>
  </NuxtLink>
</template>

<style scoped>
.timing { display: block; text-decoration: none; color: inherit; cursor: pointer; }
.timing:hover { box-shadow: 0 1px 2px rgba(33,36,61,.08), 0 8px 26px rgba(33,36,61,.1); }
.head { display: flex; align-items: baseline; }
.head-label { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.head-chip { margin-left: auto; font-weight: 700; color: var(--primary-hover); background: var(--tint); }
.days { display: flex; gap: 5px; margin-top: 14px; }
.day { flex: 1; height: 26px; border-radius: 8px; display: flex; align-items: center; justify-content: center; }
.day-count { font-size: 9px; font-weight: 700; color: #fff; }
.dates { display: flex; gap: 5px; margin-top: 5px; }
.date { flex: 1; text-align: center; font-size: 8.5px; font-weight: 500; color: #a8adc7; }
.date.ovu { font-weight: 700; color: var(--plum-ink); }
.note { display: flex; align-items: center; gap: 6px; margin-top: 9px; font-size: 10.5px; font-weight: 500; color: var(--muted); }
.note-mark { width: 8px; height: 8px; border-radius: 2px; box-shadow: inset 0 0 0 2px var(--primary); }
.summary { display: flex; align-items: center; margin-top: 9px; font-size: 12px; color: var(--ink-2); }
.details { margin-left: auto; font-weight: 700; color: var(--primary); }
</style>
