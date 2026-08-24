<script setup lang="ts">
import type { CalendarMonth, DailyLog, ImportResult } from '~/types/api'
import { CAL_COLORS, FIELD_LABELS, FIELD_ORDER } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'
import { formatDateShort, monthTitle } from '~/utils/format'

const store = useAppStore()
const api = useApi()

const current = ref<CalendarMonth | null>(null)
const selectedDate = ref<string | null>(null)
const selectedLog = ref<DailyLog | null>(null)
const month = ref('') // "2026-08"

function ym(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`
}

async function loadMonth(value: string) {
  const [y, m] = value.split('-').map(Number)
  current.value = await api.calendar(y!, m!)
  month.value = value
}

async function select(date: string) {
  selectedDate.value = date
  selectedLog.value = await api.log(date)
}

onMounted(async () => {
  const today = new Date()
  await loadMonth(ym(today))
  const iso = current.value!.days.find(d => d.isToday)?.date
  if (iso) await select(iso)
})
watch(() => store.refreshTick, async () => {
  if (month.value) await loadMonth(month.value)
  if (selectedDate.value) await select(selectedDate.value)
})

const monthOptions = computed(() => {
  if (!current.value) return []
  const options: string[] = []
  const [fy, fm] = current.value.range.firstMonth.split('-').map(Number)
  const [ly, lm] = current.value.range.lastMonth.split('-').map(Number)
  const cursor = new Date(fy!, fm! - 1, 1)
  const last = new Date(ly!, lm! - 1, 1)
  while (cursor <= last) { options.push(ym(cursor)); cursor.setMonth(cursor.getMonth() + 1) }
  return options
})
const canPrev = computed(() => monthOptions.value.indexOf(month.value) > 0)
const canNext = computed(() => {
  const i = monthOptions.value.indexOf(month.value)
  return i >= 0 && i < monthOptions.value.length - 1
})
function shift(delta: number) {
  const i = monthOptions.value.indexOf(month.value)
  const next = monthOptions.value[i + delta]
  if (next) void loadMonth(next)
}

const WEEK_HEADS = ['H', 'K', 'Sz', 'Cs', 'P', 'Szo', 'V']
const leadingBlanks = computed(() => {
  if (!current.value) return 0
  const first = new Date(`${current.value.days[0]!.date}T00:00:00`)
  return (first.getDay() + 6) % 7
})
const LEGEND = [
  { label: 'Menstruáció', bg: CAL_COLORS.menstruation.bg },
  { label: 'Termékeny', bg: CAL_COLORS.fertile.bg },
  { label: 'Ovulációs ablak', bg: CAL_COLORS.ovulation.bg },
  { label: 'Luteális', bg: CAL_COLORS.luteal.bg },
  { label: 'Ma', bg: 'var(--primary)' },
]

const selRows = computed(() => FIELD_ORDER.map((key, i) => ({
  key, i, label: FIELD_LABELS[key], value: fieldValue(selectedLog.value, key),
})))
const selHasAny = computed(() => selRows.value.some(r => r.value !== null))
const selectedDay = computed(() =>
  current.value?.days.find(d => d.date === selectedDate.value) ?? null)
const isFutureSelected = computed(() => {
  // fallback amíg az overview betölt; UTC-eltérés legfeljebb átmeneti
  const today = store.overview?.today ?? new Date().toISOString().slice(0, 10)
  return !!(selectedDate.value && selectedDate.value > today)
})
const dayNum = (iso: string) => Number(iso.slice(8))

// --- Period Tracker PDF import ---
const importInput = ref<HTMLInputElement | null>(null)
const importFile = ref<File | null>(null)
const importPreview = ref<ImportResult | null>(null)
const importBusy = ref(false)
const importError = ref<string | null>(null)
const importDone = ref<ImportResult | null>(null)

async function pickImportFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0] ?? null
  if (!file) return
  importFile.value = file
  importPreview.value = null
  importDone.value = null
  importError.value = null
  importBusy.value = true
  try {
    importPreview.value = await api.importPcReport(file, true)
  }
  catch {
    importError.value = 'A fájl nem dolgozható fel PDF-riportként.'
    importFile.value = null
  }
  finally {
    importBusy.value = false
    if (importInput.value) importInput.value.value = ''
  }
}

async function applyImport() {
  if (!importFile.value) return
  importBusy.value = true
  importError.value = null
  try {
    importDone.value = await api.importPcReport(importFile.value, false)
    importPreview.value = null
    importFile.value = null
    store.refresh()
    if (month.value) await loadMonth(month.value)
  }
  catch {
    importError.value = 'Az importálás nem sikerült — próbáld újra.'
  }
  finally {
    importBusy.value = false
  }
}

function cancelImport() {
  importFile.value = null
  importPreview.value = null
  importError.value = null
}
</script>

<template>
  <div v-if="current" class="stack">
    <div class="card">
      <div class="nav">
        <button class="nav-btn" :disabled="!canPrev" aria-label="Előző hónap" @click="shift(-1)">‹</button>
        <select class="nav-select" :value="month" @change="loadMonth(($event.target as HTMLSelectElement).value)">
          <option v-for="option in monthOptions" :key="option" :value="option">{{ monthTitle(option) }}</option>
        </select>
        <button class="nav-btn" :disabled="!canNext" aria-label="Következő hónap" @click="shift(1)">›</button>
      </div>
      <div v-if="current.cycleDayOfToday" class="nav-sub">ciklus {{ current.cycleDayOfToday }}. nap</div>
      <div v-else-if="!current.hasData" class="nav-sub dim">Ehhez a hónaphoz még nincs rögzített adat</div>

      <div class="grid">
        <div v-for="w in WEEK_HEADS" :key="w" class="weekhead">{{ w }}</div>
        <div v-for="i in leadingBlanks" :key="`blank-${i}`" />
        <button v-for="day in current.days" :key="day.date" class="cell" :style="{
          background: day.date === selectedDate ? 'var(--primary)' : CAL_COLORS[day.category].bg,
          color: day.date === selectedDate ? '#ffffff' : CAL_COLORS[day.category].fg,
          boxShadow: day.isToday && day.date !== selectedDate ? 'inset 0 0 0 2px var(--primary)' : 'none',
        }" @click="select(day.date)">
          <span class="cell-num" :class="{ bold: day.isToday || day.date === selectedDate }">{{ dayNum(day.date) }}</span>
          <span class="cell-dots">
            <span v-if="day.hasBbt" class="cell-dot" :style="{ background: day.date === selectedDate ? '#fff' : '#7c82a6' }" />
            <span v-if="day.intercourseCount > 0" class="cell-dot" :style="{ background: day.date === selectedDate ? '#fff' : 'var(--primary)' }" />
          </span>
        </button>
      </div>

      <div class="legend">
        <div v-for="item in LEGEND" :key="item.label" class="legend-item">
          <span class="legend-dot" :style="{ background: item.bg }" />
          <span>{{ item.label }}</span>
        </div>
      </div>
    </div>

    <div v-if="selectedDate" class="card">
      <div class="sel-head">
        <span class="section-title">{{ monthTitle(selectedDate.slice(0, 7)).split('. ')[1] }} {{ dayNum(selectedDate) }}.</span>
        <span class="chip sel-chip">{{ selectedDay?.cycleDay ? `${selectedDay.cycleDay}. ciklusnap` : 'cikluson kívül' }}</span>
      </div>
      <div v-if="isFutureSelected" class="sel-empty">Ez a nap még előttünk áll — bejegyzés majd aznap rögzíthető.</div>
      <div v-else-if="!selHasAny" class="sel-empty">Ezen a napon nincs bejegyzés.
        <button class="sel-add" @click="store.openSheet(selectedDate, 0, false)">Rögzítés</button>
      </div>
      <div v-else class="sel-rows">
        <button v-for="row in selRows" :key="row.key" class="sel-row" :class="{ set: row.value !== null }"
          @click="store.openSheet(selectedDate, row.i, true)">
          <span class="sel-label">{{ row.label }}</span>
          <span class="sel-value" :class="{ set: row.value !== null }">{{ row.value ?? 'nincs adat' }}</span>
          <span class="sel-action">{{ row.value !== null ? 'módosítás' : 'rögzítés' }}</span>
        </button>
      </div>
    </div>

    <div class="card">
      <div class="section-title">Importálás</div>
      <div class="import-sub">Period Tracker / Period Calendar PDF-riport beolvasása: cikluskezdetek,
        menstruáció-napok és ovulációs tesztek. A meglévő bejegyzéseket nem írja felül.</div>

      <input ref="importInput" type="file" accept="application/pdf,.pdf" hidden @change="pickImportFile">

      <div v-if="importError" class="import-error">{{ importError }}</div>

      <template v-if="importPreview">
        <div class="import-preview">
          <div class="import-line"><b>{{ importPreview.cyclesFound }}</b> ciklus
            <template v-if="importPreview.from"> ({{ formatDateShort(importPreview.from) }} – {{ formatDateShort(importPreview.to!) }})</template>
            · <b>{{ importPreview.lhTestCount }}</b> LH-teszt</div>
          <div class="import-line"><b>{{ importPreview.daysWritten }}</b> nap íródna
            <template v-if="importPreview.fieldsSkipped > 0"> · {{ importPreview.fieldsSkipped }} mező kihagyva (már van adat)</template></div>
          <div v-for="(w, i) in importPreview.warnings" :key="i" class="import-warning">⚠ {{ w }}</div>
        </div>
        <div class="import-actions">
          <button class="btn btn-ghost import-btn" :disabled="importBusy" @click="cancelImport">Mégse</button>
          <button class="btn btn-primary import-btn" :disabled="importBusy || importPreview.daysWritten === 0"
            @click="applyImport">Importálás</button>
        </div>
      </template>

      <template v-else-if="importDone">
        <div class="import-preview">
          <div class="import-line">✓ Importálva: <b>{{ importDone.daysWritten }}</b> nap,
            <b>{{ importDone.cyclesFound }}</b> ciklus, <b>{{ importDone.lhTestCount }}</b> LH-teszt.</div>
          <div v-for="(w, i) in importDone.warnings" :key="i" class="import-warning">⚠ {{ w }}</div>
        </div>
        <button class="btn btn-ghost import-btn" @click="importDone = null">Rendben</button>
      </template>

      <button v-else class="btn btn-ghost import-btn" :disabled="importBusy" @click="importInput?.click()">
        {{ importBusy ? 'Feldolgozás…' : 'PDF kiválasztása' }}
      </button>
    </div>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.nav { display: flex; align-items: center; gap: 8px; }
.nav-btn {
  width: 34px; height: 34px; flex-shrink: 0; border-radius: 10px; border: 0; background: #f5f7fe;
  color: var(--ink-2); font: 700 15px 'Montserrat', sans-serif; cursor: pointer;
}
.nav-btn:disabled { opacity: .4; cursor: default; }
.nav-btn:not(:disabled):hover { background: var(--tint); }
.nav-select {
  flex: 1; min-width: 0; text-align: center; font: 700 13px 'Montserrat', sans-serif; color: var(--ink);
  border: 0; background: #f5f7fe; border-radius: 10px; padding: 9px 4px; cursor: pointer;
}
.nav-sub { text-align: center; margin-top: 8px; font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.nav-sub.dim { color: var(--muted); }
.grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 5px; margin: 16px auto 0; max-width: 440px; }
.weekhead { text-align: center; font-size: 10.5px; font-weight: 600; color: var(--ink-3); padding-bottom: 4px; }
.cell {
  aspect-ratio: 1; border-radius: 12px; border: 0; cursor: pointer; font-family: inherit;
  display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 3px;
}
.cell-num { font-size: 12.5px; font-weight: 500; }
.cell-num.bold { font-weight: 700; }
.cell-dots { display: flex; gap: 3px; height: 4px; }
.cell-dot { width: 4px; height: 4px; border-radius: 99px; }
.legend { display: flex; flex-wrap: wrap; gap: 13px; margin-top: 18px; }
.legend-item { display: flex; align-items: center; gap: 7px; font-size: 11.5px; font-weight: 500; color: var(--ink-2); }
.legend-dot { width: 10px; height: 10px; border-radius: 3px; }
.sel-head { display: flex; align-items: baseline; }
.sel-chip { margin-left: auto; color: var(--primary); background: var(--tint); font-size: 11.5px; }
.sel-empty { margin-top: 14px; padding: 26px 14px; border-radius: 14px; background: #f5f7fe; text-align: center; font-size: 13px; color: var(--ink-2); }
.sel-add { display: block; margin: 12px auto 0; border: 0; background: var(--tint); color: var(--primary-deep); font: 700 12px 'Montserrat', sans-serif; border-radius: 99px; padding: 9px 18px; cursor: pointer; }
.import-sub { font-size: 12.5px; color: var(--ink-3); line-height: 1.55; margin-top: 6px; }
.import-error { margin-top: 12px; font-size: 12.5px; color: #b3261e; }
.import-preview { margin-top: 14px; background: var(--surface); border-radius: 14px; padding: 13px 14px; display: flex; flex-direction: column; gap: 6px; }
.import-line { font-size: 13px; color: var(--ink-2); line-height: 1.5; }
.import-warning { font-size: 12px; color: #8a5a00; }
.import-actions { display: flex; gap: 10px; margin-top: 12px; }
.import-btn { margin-top: 12px; padding: 13px 0; font-size: 13px; }
.import-actions .import-btn { flex: 1; margin-top: 0; }
.sel-rows { display: flex; flex-direction: column; gap: 2px; margin-top: 12px; }
.sel-row {
  display: flex; align-items: center; padding: 12px 14px; border-radius: 12px; border: 0;
  background: transparent; cursor: pointer; font-family: inherit; text-align: left;
}
.sel-row.set { background: #f5f7fe; }
.sel-row:hover { background: var(--tint); }
.sel-label { font-size: 12.5px; font-weight: 500; color: var(--ink-2); width: 96px; flex-shrink: 0; }
.sel-value { font-size: 14px; font-weight: 500; color: var(--ink-4); min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.sel-value.set { font-weight: 600; color: var(--ink); }
.sel-action { margin-left: auto; flex-shrink: 0; padding-left: 8px; font-size: 11px; font-weight: 700; color: var(--primary); }
</style>
