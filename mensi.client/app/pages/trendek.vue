<script setup lang="ts">
import type { DailyLog, Trends } from '~/types/api'

const store = useAppStore()
const api = useApi()
const trends = ref<Trends | null>(null)
const cycleLogs = ref<DailyLog[]>([])

async function load() {
  trends.value = await api.trends()
  const rows = trends.value.bbt?.rows
  if (rows && rows.length > 0)
    cycleLogs.value = (await api.logs(rows[0]!.date, rows[rows.length - 1]!.date)).days
  else cycleLogs.value = []
}
watch(() => store.refreshTick, load, { immediate: true })
</script>

<template>
  <div v-if="trends" class="stack">
    <div v-if="!trends.stats" class="card empty">
      Statisztikához legalább egy lezárt ciklus kell.
    </div>
    <TrendekCycleStatsCard v-if="trends.stats" :trends="trends" />
    <TrendekBbtTableCard v-if="trends.bbt" :bbt="trends.bbt" />
    <TrendekEntriesGridCard v-if="trends.bbt" :rows="trends.bbt.rows" :logs="cycleLogs" />
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.empty { font-size: 13px; color: var(--ink-2); }
</style>
