<script setup lang="ts">
import type { Overview } from '~/types/api'
import { FIELD_ORDER, FIELD_LABELS } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'
import { formatDateShort } from '~/utils/format'
import { addDays } from '~/utils/format'

const props = defineProps<{ overview: Overview }>()
const chips = computed(() => {
  const log = props.overview.yesterdayLog
  const items: string[] = []
  for (const key of FIELD_ORDER) {
    const value = fieldValue(log, key)
    if (value === null) continue
    items.push(key === 'bbt' ? value : `${FIELD_LABELS[key]}: ${value}`)
  }
  return items
})
const dateLabel = computed(() => formatDateShort(addDays(props.overview.today, -1)))
</script>

<template>
  <div class="card">
    <div class="title">Tegnap · {{ dateLabel }}</div>
    <div class="chips">
      <span v-if="chips.length === 0" class="chip empty">Nincs bejegyzés</span>
      <span v-for="c in chips" :key="c" class="chip item">{{ c }}</span>
    </div>
  </div>
</template>

<style scoped>
.title { font-size: 12.5px; font-weight: 600; color: var(--ink-3); }
.chips { display: flex; flex-wrap: wrap; gap: 7px; margin-top: 11px; }
.chip { font-size: 12px; padding: 6px 12px; }
.item { color: var(--plum-ink); background: var(--tint); }
.empty { color: var(--ink-3); background: var(--surface); }
</style>
