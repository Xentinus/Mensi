<script setup lang="ts">
import type { Overview } from '~/types/api'
import { FIELD_ORDER, FIELD_LABELS } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'

const props = defineProps<{ overview: Overview }>()
const store = useAppStore()
const rows = computed(() => FIELD_ORDER.map((key, i) => {
  const value = fieldValue(props.overview.todayLog, key)
  return { key, i, label: FIELD_LABELS[key], value }
}))
const filled = computed(() => rows.value.filter(r => r.value !== null).length)
</script>

<template>
  <div class="card">
    <div class="head">
      <span class="section-title">Mai bejegyzés</span>
      <span class="count">{{ filled }} / 7 kitöltve</span>
    </div>
    <div class="sub">Külön-külön is rögzíthető — csak azt töltsd ki, ami ma történt.</div>
    <div class="rows">
      <button v-for="row in rows" :key="row.key" class="row"
        :class="{ filled: row.value !== null }"
        @click="store.openSheet(overview.today, row.i, true)">
        <span class="row-label">{{ row.label }}</span>
        <span class="row-value" :class="{ set: row.value !== null }">{{ row.value ?? 'nincs rögzítve' }}</span>
        <span class="row-action">{{ row.value !== null ? 'módosítás' : 'rögzítés' }}</span>
      </button>
    </div>
    <button class="btn all" @click="store.openSheet(overview.today, 0, false)">Mind végigkérdezése</button>
  </div>
</template>

<style scoped>
.head { display: flex; align-items: center; }
.count { margin-left: auto; font-size: 11.5px; font-weight: 600; color: var(--ink-3); }
.sub { font-size: 12px; color: var(--ink-3); margin-top: 5px; line-height: 1.45; }
.rows { display: flex; flex-direction: column; gap: 3px; margin-top: 13px; }
.row {
  display: flex; align-items: center; gap: 12px; padding: 12px 13px; border-radius: 12px;
  background: #f7f8fd; border: 0; cursor: pointer; font-family: inherit; text-align: left;
}
.row.filled { background: var(--tint); }
.row:hover { background: var(--tint); }
.row-label { font-size: 12.5px; font-weight: 600; color: var(--ink-2); width: 84px; flex-shrink: 0; }
.row-value { font-size: 13.5px; font-weight: 500; color: var(--ink-4); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.row-value.set { font-weight: 700; color: var(--ink); }
.row-action { margin-left: auto; flex-shrink: 0; font-size: 11.5px; font-weight: 700; color: var(--primary); white-space: nowrap; }
.all { margin-top: 13px; background: var(--tint); color: var(--primary-deep); padding: 15px 0; font-size: 13px; }
.all:hover { background: #dfe4ff; }
</style>
