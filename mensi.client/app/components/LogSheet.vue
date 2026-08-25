<script setup lang="ts">
import type { CervicalMucus, CrampType, DailyLog, FlowIntensity, LogPatch, Mood } from '~/types/api'
import { CRAMP_SEVERITY_LABELS, CRAMP_TYPE_LABELS, CRAMP_TYPE_ORDER, FIELD_LABELS, FIELD_ORDER, FLOW_LABELS, FLOW_ORDER, MOOD_EMOJI, MOOD_LABELS, MOOD_ORDER, MUCUS_LABELS, MUCUS_ORDER, formatLhValue, lhBand } from '~/utils/labels'
import { fieldValue } from '~/utils/fieldValue'
import { formatTemp } from '~/utils/format'
import { composeBbt, decomposeBbt } from '~/utils/bbt'

const store = useAppStore()
const api = useApi()

const STEPS = ['Testhő', 'Nyák', 'LH-teszt', 'Görcs', 'Folyás', 'Együttlét', 'Hangulat', 'Összegzés']

const before = ref<DailyLog | null>(null)
const step = ref(0)
const skipped = ref<Set<number>>(new Set())
const touched = ref<Set<number>>(new Set())

// mezőállapot
const whole = ref(36); const tenths = ref(3); const hundredths = ref(6); const tempSet = ref(false)
const mucus = ref<CervicalMucus | null>(null)
// Az LH a tesztcsík/kontrollcsík aránya 0–1 között. A háromértékű besorolást a szerver
// vezeti vissza belőle — a kliens csak az arányt küldi.
const lh = ref<number | null>(null)
const LH_STEP = 0.05
const lhBandOf = computed(() => lhBand(lh.value ?? 0))
const crampType = ref<CrampType | null>(null)
const crampSeverity = ref<number | null>(null)
const flow = ref<FlowIntensity | null>(null)
const periodStart = ref(false)
const sexEvents = ref<{ protected: boolean | null }[]>([])
const sexTouched = ref(false)
const moods = ref<Mood[]>([])

const bbtValue = computed(() => composeBbt(whole.value, tenths.value, hundredths.value))

watch(() => store.sheetOpen, async (open) => {
  if (!open || !store.sheetDate) return
  const date = store.sheetDate
  step.value = store.sheetStep
  skipped.value = new Set()
  touched.value = new Set()

  // Szinkron reset ELŐSZÖR: amíg az api.log() függ, a sheet már látszik, de ne az előző
  // session (más nap) értékeit mutassa, és egy korai koppintás se egy mindjárt felülírt
  // állapotra landoljon.
  before.value = null
  whole.value = 36; tenths.value = 3; hundredths.value = 6; tempSet.value = false
  mucus.value = null
  lh.value = null
  crampType.value = null
  crampSeverity.value = null
  flow.value = null
  periodStart.value = false
  sexEvents.value = []
  sexTouched.value = false
  moods.value = []

  const log = await api.log(date)
  // Ha közben bezárták a sheetet, vagy másik napra nyitották újra, ez a betöltés már elavult.
  if (!store.sheetOpen || store.sheetDate !== date) return

  before.value = log
  tempSet.value = log.bbtCelsius !== null
  if (log.bbtCelsius !== null) {
    const d = decomposeBbt(log.bbtCelsius)
    whole.value = d.whole; tenths.value = d.tenths; hundredths.value = d.hundredths
  } else { whole.value = 36; tenths.value = 3; hundredths.value = 6 }
  mucus.value = log.cervicalMucus
  lh.value = log.lhValue
  crampType.value = log.crampType
  crampSeverity.value = log.crampSeverity
  flow.value = log.flowIntensity
  periodStart.value = log.periodStart
  sexEvents.value = log.intercourse.map(e => ({ protected: e.protected }))
  sexTouched.value = log.intercourse.length > 0
  moods.value = [...log.moods]
})

function touch(i: number) { touched.value.add(i); skipped.value.delete(i) }

function buildPatch(only?: number): LogPatch {
  const patch: LogPatch = {}
  const include = (i: number) => (only === undefined ? touched.value.has(i) : only === i)
  if (include(0)) patch.bbtCelsius = tempSet.value ? bbtValue.value : null
  if (include(1)) patch.cervicalMucus = mucus.value
  if (include(2)) patch.lhValue = lh.value
  if (include(3)) { patch.crampType = crampType.value; patch.crampSeverity = crampSeverity.value }
  if (include(4)) { patch.flowIntensity = flow.value; patch.periodStart = periodStart.value }
  if (include(6)) patch.moods = moods.value
  return patch
}

async function save() {
  const date = store.sheetDate!
  const single = store.sheetSingle
  const active = step.value
  store.closeSheet()
  if (single && active === 5) {
    await store.saveIntercourse(date, sexEvents.value, before.value)
    return
  }
  if (single) {
    await store.saveLog(date, buildPatch(active), before.value)
    return
  }
  const patch = buildPatch()
  await store.saveDay(
    date,
    Object.keys(patch).length > 0 ? patch : null,
    touched.value.has(5) ? sexEvents.value : null,
    before.value,
  )
}

function next() {
  if (store.sheetSingle || step.value === 7) { void save(); return }
  step.value = Math.min(step.value + 1, 7)
}
function skip() {
  skipped.value.add(step.value)
  step.value = Math.min(step.value + 1, 6)
}
const summaryRows = computed(() => {
  const preview: DailyLog = {
    ...(before.value ?? {
      date: store.sheetDate ?? '', bbtOutlier: false, updatedAt: null, updatedBy: null,
      bbtCelsius: null, cervicalMucus: null, lhTest: null, lhValue: null, crampType: null,
      crampSeverity: null, flowIntensity: null, periodStart: false, moods: [], intercourse: [],
    }),
    bbtCelsius: tempSet.value ? bbtValue.value : null,
    cervicalMucus: mucus.value,
    lhValue: lh.value,
    crampType: crampType.value,
    crampSeverity: crampSeverity.value,
    flowIntensity: flow.value,
    periodStart: periodStart.value,
    moods: moods.value,
    intercourse: sexEvents.value.map((e, i) => ({ id: i, protected: e.protected })),
  }
  return FIELD_ORDER.map((key, i) => ({ i, label: FIELD_LABELS[key], value: fieldValue(preview, key) }))
})
</script>

<template>
  <Teleport to="body">
    <div v-if="store.sheetOpen" class="overlay">
      <div class="backdrop" @click="store.closeSheet()" />
      <div class="box">
        <div class="head">
          <div class="grip" />
          <div class="head-row">
            <span class="section-title">{{ STEPS[step] }}</span>
            <span class="count">{{ store.sheetSingle ? 'egy mező' : `${step + 1} / 8` }}</span>
            <button class="close" aria-label="Bezárás" @click="store.closeSheet()">✕</button>
          </div>
          <div v-if="!store.sheetSingle" class="dots">
            <button v-for="(s, i) in STEPS" :key="s" class="dot"
              :class="{ current: i === step, done: i < step && !skipped.has(i), skipped: skipped.has(i) }"
              @click="step = i" />
          </div>
        </div>

        <div class="body noscroll">
          <!-- 0: Testhő -->
          <div v-if="step === 0">
            <div class="step-title">Testhő</div>
            <div class="step-sub">Nem kötelező.
              <template v-if="store.overview?.yesterdayLog?.bbtCelsius">
                Tegnap: {{ formatTemp(store.overview.yesterdayLog.bbtCelsius) }}</template>
            </div>
            <div class="wheels" @click="tempSet = true; touch(0)">
              <WheelPicker v-model="whole" :options="[35, 36, 37, 38]" width="80px" @update:model-value="tempSet = true; touch(0)" />
              <div class="wheel-sep">,</div>
              <WheelPicker v-model="tenths" :options="[0,1,2,3,4,5,6,7,8,9]" @update:model-value="tempSet = true; touch(0)" />
              <WheelPicker v-model="hundredths" :options="[0,1,2,3,4,5,6,7,8,9]" @update:model-value="tempSet = true; touch(0)" />
              <div class="wheel-unit">°C</div>
            </div>
          </div>

          <!-- 1: Nyák -->
          <div v-else-if="step === 1">
            <div class="step-title">Cervikális nyák</div>
            <div class="step-sub">Szárazból nyúlósba — a nyúlós a legtermékenyebb.</div>
            <div class="mucus-row">
              <button v-for="(key, i) in MUCUS_ORDER" :key="key" class="mucus-opt"
                @click="mucus = key; touch(1)">
                <div class="mucus-swatch" :class="{ active: mucus === key }"
                  :style="{ background: mucus === key ? 'var(--primary)' : ['#f2f6ff', '#dfe9ff', '#c6d6ff', '#aac4ff'][i] }" />
                <div class="opt-label" :class="{ active: mucus === key }">{{ MUCUS_LABELS[key] }}</div>
              </button>
            </div>
          </div>

          <!-- 2: LH -->
          <div v-else-if="step === 2">
            <div class="step-title">LH-teszt</div>
            <div class="step-sub">A tesztcsík sötétsége a kontrollcsíkhoz képest.
              <template v-if="formatLhValue(store.overview?.yesterdayLog?.lhValue)">
                Tegnap: {{ formatLhValue(store.overview!.yesterdayLog!.lhValue) }}</template>
            </div>
            <div class="lh-strip" :class="{ empty: lh === null }">
              <div class="lh-line control" />
              <div class="lh-line test" :style="{ opacity: Math.max(lh ?? 0, 0.04) }" />
              <div class="lh-strip-caps"><span>kontroll</span><span>teszt</span></div>
            </div>
            <div class="lh-readout">
              <template v-if="lh === null">
                <span class="lh-band idle">Nincs rögzítve</span>
              </template>
              <template v-else>
                <span class="lh-value">{{ lh.toFixed(2).replace('.', ',') }}</span>
                <span class="lh-band">{{ lhBandOf.label }}</span>
              </template>
            </div>
            <div class="lh-note-line">{{ lh === null ? 'Húzd a csúszkát a leolvasott arányhoz.' : lhBandOf.note }}</div>
            <input class="lh-range" type="range" min="0" max="1" :step="LH_STEP"
              :value="lh ?? 0" aria-label="LH tesztcsík aránya"
              @input="lh = Number(($event.target as HTMLInputElement).value); touch(2)">
            <div class="lh-scale"><span>0,00</span><span>0,50</span><span>1,00</span></div>
            <button v-if="lh !== null" class="lh-clear" @click="lh = null; touch(2)">Nem teszteltem</button>
          </div>

          <!-- 3: Görcs -->
          <div v-else-if="step === 3">
            <div class="step-title">Görcs</div>
            <div class="step-sub">Előbb a hely, aztán az erőssége.</div>
            <div class="cramp-types">
              <button v-for="key in CRAMP_TYPE_ORDER" :key="key" class="cramp-type"
                :class="{ active: crampType === key, disabled: crampSeverity === 0 }"
                :disabled="crampSeverity === 0"
                @click="crampType = key; if (crampSeverity === null) crampSeverity = 1; touch(3)">
                {{ CRAMP_TYPE_LABELS[key] }}
              </button>
            </div>
            <div v-if="crampSeverity === 0" class="cramp-hint">Nincs görcs esetén nincs mit kiválasztani.</div>
            <div class="scale-label">Intenzitás</div>
            <div class="scale">
              <button v-for="(label, i) in CRAMP_SEVERITY_LABELS" :key="label" class="scale-opt"
                @click="crampSeverity = i; if (i === 0) crampType = null; touch(3)">
                <div class="scale-bar" :class="{ active: crampSeverity === i }"
                  :style="{ height: `${24 + i * 15}px`,
                    background: crampSeverity === i ? 'var(--primary)' : `rgba(90,92,214,${0.1 + i * 0.16})` }" />
                <div class="opt-label" :class="{ active: crampSeverity === i }">{{ label }}</div>
              </button>
            </div>
          </div>

          <!-- 4: Folyás -->
          <div v-else-if="step === 4">
            <div class="step-title">Folyás</div>
            <div class="scale">
              <button v-for="(key, i) in FLOW_ORDER" :key="key" class="scale-opt"
                @click="flow = key; touch(4)">
                <div class="scale-bar" :class="{ active: flow === key }"
                  :style="{ height: `${22 + i * 14}px`,
                    background: flow === key ? 'var(--plum)' : `rgba(150,152,226,${0.12 + i * 0.17})` }" />
                <div class="opt-label" :class="{ active: flow === key }">{{ FLOW_LABELS[key] }}</div>
              </button>
            </div>
            <button class="period-toggle" :class="{ active: periodStart }" @click="periodStart = !periodStart; touch(4)">
              <span class="period-box" :class="{ active: periodStart }">{{ periodStart ? '✓' : '' }}</span>
              <span>
                <span class="period-title">Ma kezdődött a menstruáció</span>
                <span class="period-sub">Ez lesz az új ciklus 1. napja.</span>
              </span>
            </button>
          </div>

          <!-- 5: Együttlét -->
          <div v-else-if="step === 5">
            <div class="step-title">Együttlét</div>
            <div class="step-sub">Ez az egyetlen adat, ami a fogamzási esélybe számít. Egy napon több is lehet.</div>
            <div class="sex-box" :class="{ active: sexEvents.length > 0 }">
              <div>
                <div class="sex-label">{{ sexEvents.length === 0 ? (sexTouched ? 'Ma nem volt' : 'Nincs rögzítve')
                  : sexEvents.length === 1 ? 'Egy alkalom' : `${sexEvents.length} alkalom` }}</div>
                <div class="sex-note">{{ sexTouched ? 'rögzítve lesz mentéskor' : 'állítsd be a mai számot' }}</div>
              </div>
              <div class="sex-controls">
                <button class="sex-btn minus" aria-label="Kevesebb"
                  @click="sexEvents = sexEvents.slice(0, -1); sexTouched = true; touch(5)">−</button>
                <span class="sex-count">{{ sexEvents.length }}</span>
                <button class="sex-btn plus" aria-label="Több"
                  @click="if (sexEvents.length < 6) sexEvents = [...sexEvents, { protected: false }]; sexTouched = true; touch(5)">+</button>
              </div>
            </div>
            <div v-if="sexEvents.length > 0" class="sex-events">
              <div v-for="(ev, i) in sexEvents" :key="i" class="sex-event">
                <span class="sex-event-label">{{ i + 1 }}. alkalom</span>
                <span class="sex-event-note">Védekezéssel volt</span>
                <button class="switch" :class="{ on: ev.protected === true }" role="switch"
                  :aria-checked="ev.protected === true"
                  @click="sexEvents[i] = { protected: ev.protected !== true }; touch(5)">
                  <span class="knob" />
                </button>
              </div>
            </div>
            <div class="sex-footnote">A 0 azt jelenti, hogy ma nem volt együttlét — ez is rögzített adat, nem hiányzó.</div>
          </div>

          <!-- 6: Hangulat -->
          <div v-else-if="step === 6">
            <div class="step-title">Hangulat</div>
            <div class="step-sub">Ez is jelezheti az ovulációt — több is kiválasztható.</div>
            <div class="moods">
              <button v-for="key in MOOD_ORDER" :key="key" class="mood-chip"
                :class="{ active: moods.includes(key) }"
                @click="moods = moods.includes(key) ? moods.filter(m => m !== key) : [...moods, key]; touch(6)">
                {{ MOOD_EMOJI[key] }} {{ MOOD_LABELS[key] }}
              </button>
            </div>
          </div>

          <!-- 7: Összegzés -->
          <div v-else>
            <div class="step-title">Összegzés</div>
            <div class="step-sub">Bármelyik sorra koppintva javíthatod.</div>
            <div class="summary">
              <button v-for="row in summaryRows" :key="row.i" class="summary-row" @click="step = row.i">
                <span class="summary-label">{{ row.label }}</span>
                <span class="summary-value" :class="{ set: row.value !== null }">{{ row.value ?? 'Kihagyva' }}</span>
                <span class="summary-action">{{ row.value !== null ? 'javítás' : 'kitöltés' }}</span>
              </button>
            </div>
          </div>
        </div>

        <div class="foot">
          <button v-if="store.sheetSingle" class="btn btn-ghost foot-cancel" @click="store.closeSheet()">Mégse</button>
          <button v-if="!store.sheetSingle && step > 0" class="foot-back" aria-label="Vissza"
            @click="step = Math.max(0, step - 1)">←</button>
          <button v-if="!store.sheetSingle && step < 6" class="btn btn-ghost foot-skip" @click="skip()">Kihagyom</button>
          <button class="btn btn-primary foot-next" @click="next()">
            {{ store.sheetSingle || step === 7 ? 'Mentés' : step === 6 ? 'Összegzés' : 'Tovább' }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.overlay { position: fixed; inset: 0; z-index: 50; display: flex; align-items: flex-end; justify-content: center; }
.backdrop { position: absolute; inset: 0; background: rgba(33, 36, 61, .4); }
.box {
  position: relative; width: 100%; max-width: 560px; max-height: 88vh; background: #fff;
  border-radius: 22px 22px 0 0; display: flex; flex-direction: column;
  box-shadow: 0 -10px 40px rgba(33, 36, 61, .18);
}
.head { padding: 14px 20px; flex-shrink: 0; }
.grip { width: 38px; height: 4px; border-radius: 99px; background: #d8dcec; margin: 0 auto 14px; }
.head-row { display: flex; align-items: center; }
.count { margin-left: auto; font-size: 11.5px; font-weight: 600; color: var(--ink-3); }
.close {
  margin-left: 12px; width: 28px; height: 28px; border-radius: 99px; background: var(--bg);
  border: 0; display: grid; place-items: center; font-size: 13px; color: var(--ink-2); cursor: pointer;
}
.dots { display: flex; gap: 5px; margin-top: 12px; }
.dot { flex: 1; height: 5px; border-radius: 99px; background: #e3e8fb; border: 0; cursor: pointer; padding: 0; }
.dot.done { background: var(--light-blue); }
.dot.skipped { background: #d8dcec; }
.dot.current { background: var(--primary); }
.body { flex: 1; overflow-y: auto; padding: 8px 20px 16px; }
.step-title { font-size: 21px; font-weight: 700; letter-spacing: -.02em; }
.step-sub { font-size: 13px; color: var(--ink-2); margin-top: 6px; }
.wheels { display: flex; gap: 8px; margin-top: 20px; justify-content: center; align-items: center; }
.wheel-sep { font-size: 26px; font-weight: 700; color: var(--ink-4); }
.wheel-unit { font-size: 15px; font-weight: 600; color: var(--ink-3); }
.mucus-row { display: flex; gap: 8px; margin-top: 22px; }
.mucus-opt { flex: 1; border: 0; background: none; cursor: pointer; display: flex; flex-direction: column; gap: 9px; padding: 0; }
.mucus-swatch { height: 70px; border-radius: 14px; width: 100%; }
.mucus-swatch.active { box-shadow: inset 0 0 0 3px var(--plum-ink), 0 0 0 4px rgba(90,92,214,.16); }
.opt-label { font-size: 11px; font-weight: 600; text-align: center; color: var(--ink-2); width: 100%; }
.opt-label.active { color: var(--primary); }
.lh-strip {
  margin-top: 20px; height: 74px; border-radius: 14px; background: var(--surface);
  display: flex; align-items: center; justify-content: center; gap: 34px; position: relative;
}
.lh-strip.empty { opacity: .55; }
.lh-line { width: 9px; height: 40px; border-radius: 3px; background: var(--plum-ink); }
.lh-line.test { background: var(--primary); }
.lh-strip-caps {
  position: absolute; bottom: 8px; left: 0; right: 0; display: flex; justify-content: center;
  gap: 22px; font-size: 10px; font-weight: 600; color: var(--ink-3);
}
.lh-strip-caps span { width: 43px; text-align: center; }
.lh-readout { display: flex; align-items: baseline; gap: 10px; margin-top: 18px; }
.lh-value { font-size: 30px; font-weight: 700; letter-spacing: -.02em; color: var(--ink); }
.lh-band { font-size: 14px; font-weight: 700; color: var(--primary-hover); }
.lh-band.idle { font-size: 19px; color: var(--ink-3); }
.lh-note-line { font-size: 12.5px; color: var(--ink-2); margin-top: 4px; }
.lh-range { width: 100%; margin-top: 16px; accent-color: var(--primary); }
.lh-scale {
  display: flex; justify-content: space-between; font-size: 11px; font-weight: 600;
  color: var(--ink-3); margin-top: 2px;
}
.lh-clear {
  margin-top: 14px; padding: 10px 14px; border: 0; border-radius: 12px; background: var(--surface);
  font: 600 13px 'Montserrat', sans-serif; color: var(--ink-2); cursor: pointer;
}
.lh-col { display: flex; flex-direction: column; gap: 10px; margin-top: 20px; }
.lh-opt {
  padding: 19px 18px; border-radius: 16px; background: var(--surface); border: 0; cursor: pointer;
  display: flex; align-items: center; font-family: inherit;
}
.lh-opt.active { background: var(--tint); box-shadow: inset 0 0 0 3px var(--primary), 0 0 0 4px rgba(90,92,214,.16); }
.lh-label { font-size: 15px; font-weight: 700; color: var(--ink); }
.lh-opt.active .lh-label { color: var(--primary-hover); }
.lh-note { margin-left: auto; font-size: 11.5px; font-weight: 500; color: var(--ink-3); }
.cramp-types { display: flex; gap: 9px; margin-top: 20px; }
.cramp-type {
  flex: 1; padding: 17px 0; text-align: center; border-radius: 14px; background: var(--surface);
  border: 0; cursor: pointer; font: 600 14px 'Montserrat', sans-serif; color: var(--ink);
}
.cramp-type.active { background: var(--tint); color: var(--primary); box-shadow: inset 0 0 0 3px var(--primary); }
.cramp-type.disabled { color: #c1c5db; opacity: .55; cursor: not-allowed; }
.cramp-hint { font-size: 11.5px; color: var(--muted); margin-top: 9px; }
.scale-label { font-size: 12px; font-weight: 600; color: var(--ink-3); margin-top: 24px; }
.scale { display: flex; gap: 8px; margin-top: 11px; align-items: flex-end; }
.scale-opt { flex: 1; border: 0; background: none; cursor: pointer; display: flex; flex-direction: column; gap: 9px; justify-content: flex-end; padding: 0; }
.scale-bar { border-radius: 12px; width: 100%; }
.scale-bar.active { box-shadow: inset 0 0 0 3px var(--plum-ink), 0 0 0 4px rgba(90,92,214,.16); }
.period-toggle {
  margin-top: 18px; display: flex; align-items: center; gap: 13px; padding: 16px; width: 100%;
  border-radius: 14px; background: var(--surface); border: 0; cursor: pointer; text-align: left; font-family: inherit;
}
.period-toggle.active { background: #e6e7fb; box-shadow: inset 0 0 0 3px var(--plum); }
.period-box {
  width: 22px; height: 22px; border-radius: 7px; background: #fff; flex-shrink: 0;
  box-shadow: inset 0 0 0 2px #d8dcec; display: grid; place-items: center;
  font-size: 12px; font-weight: 700; color: #fff;
}
.period-box.active { background: var(--plum); box-shadow: none; }
.period-title { display: block; font-size: 13.5px; font-weight: 600; color: var(--ink); }
.period-toggle.active .period-title { color: var(--plum-ink); }
.period-sub { display: block; font-size: 11.5px; color: var(--ink-3); margin-top: 2px; }
.sex-box {
  display: flex; align-items: center; gap: 14px; margin-top: 22px; padding: 20px;
  border-radius: 18px; background: var(--surface);
}
.sex-box.active { background: var(--tint); box-shadow: inset 0 0 0 3px var(--primary); }
.sex-label { font-size: 17px; font-weight: 700; color: var(--ink); }
.sex-box.active .sex-label { color: var(--primary); }
.sex-note { font-size: 12px; color: var(--ink-2); margin-top: 3px; }
.sex-controls { margin-left: auto; display: flex; align-items: center; gap: 10px; }
.sex-btn {
  width: 44px; height: 44px; border-radius: 99px; border: 0; cursor: pointer;
  font: 700 20px 'Montserrat', sans-serif;
}
.sex-btn.minus { background: #fff; color: var(--primary-deep); box-shadow: 0 1px 3px rgba(33,36,61,.14); }
.sex-btn.plus { background: var(--primary); color: #fff; box-shadow: 0 2px 6px rgba(90,92,214,.32); }
.sex-count { min-width: 26px; text-align: center; font-size: 24px; font-weight: 700; }
.sex-events { display: flex; flex-direction: column; gap: 8px; margin-top: 12px; }
.sex-event { display: flex; align-items: center; padding: 14px 16px; border-radius: 14px; background: var(--surface); }
.sex-event-label { font-size: 13px; font-weight: 600; }
.sex-event-note { margin-left: auto; margin-right: 10px; font-size: 12px; font-weight: 500; color: var(--ink-2); }
.switch { width: 46px; height: 27px; border-radius: 99px; background: #dde1ef; border: 0; position: relative; cursor: pointer; flex-shrink: 0; }
.switch.on { background: var(--primary); }
.knob {
  position: absolute; top: 2.5px; left: 2.5px; width: 22px; height: 22px; border-radius: 99px;
  background: #fff; box-shadow: 0 1px 3px rgba(33,36,61,.25); transition: left .15s;
}
.switch.on .knob { left: 21.5px; }
.sex-footnote { font-size: 12px; color: var(--ink-3); margin-top: 12px; line-height: 1.5; }
.moods { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 20px; }
.mood-chip {
  padding: 12px 16px; border-radius: 99px; background: var(--surface); border: 0; cursor: pointer;
  font: 600 13.5px 'Montserrat', sans-serif; color: var(--ink); white-space: nowrap;
}
.mood-chip.active { background: var(--tint); color: var(--primary); box-shadow: inset 0 0 0 3px var(--primary); }
.summary { display: flex; flex-direction: column; gap: 3px; margin-top: 16px; }
.summary-row {
  display: flex; align-items: center; padding: 14px; border-radius: 13px; border: 0;
  background: #fafbff; cursor: pointer; font-family: inherit; text-align: left;
}
.summary-row:nth-child(odd) { background: var(--surface); }
.summary-label { font-size: 12px; font-weight: 500; color: var(--ink-3); width: 92px; flex-shrink: 0; }
.summary-value { font-size: 14px; font-weight: 500; color: var(--ink-4); }
.summary-value.set { font-weight: 600; color: var(--ink); }
.summary-action { margin-left: auto; font-size: 11.5px; font-weight: 600; color: var(--primary); }
.foot { padding: 14px 20px 22px; display: flex; gap: 10px; flex-shrink: 0; background: #fff; }
.foot-cancel { flex: 1; }
.foot-back {
  width: 56px; flex-shrink: 0; background: var(--bg); border: 0; border-radius: 99px;
  color: var(--ink-2); font: 700 16px 'Montserrat', sans-serif; cursor: pointer;
}
.foot-skip { flex: 1; }
.foot-next { flex: 2; }
</style>
