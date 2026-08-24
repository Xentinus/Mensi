<script setup lang="ts">
const store = useAppStore()
const STEPS = [
  'Rögzítsd a testhőt minden reggel, felkelés előtt — ez adja a görbe gerincét.',
  'Jelöld a menstruáció első napját; innen indul a ciklusszámítás.',
  'A nyák és az LH-teszt gyorsítja az ovuláció felismerését, de nem kötelező.',
]
</script>

<template>
  <div class="stack">
    <div class="intro">
      <div class="intro-tag">Nincs még adat</div>
      <div class="intro-title">Előrejelzéshez legalább egy teljes ciklus kell.</div>
      <div class="intro-body">Amíg nincs elég mérés, az app nem mutat becsült ovulációt és
        menstruációt — inkább semmit, mint pontatlant.</div>
    </div>
    <div v-for="(txt, i) in STEPS" :key="i" class="card step">
      <div class="step-n">{{ i + 1 }}</div>
      <div class="step-txt">{{ txt }}</div>
    </div>
    <button class="btn btn-primary" @click="store.openSheet(store.overview!.today)">
      Első bejegyzés rögzítése
    </button>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 12px; }
.intro { background: var(--tint); border-radius: 20px; padding: 22px 18px; }
.intro-tag { font-size: 12.5px; font-weight: 600; color: var(--primary); }
.intro-title { font-size: 22px; font-weight: 700; margin-top: 8px; letter-spacing: -.02em; line-height: 1.25; }
.intro-body { font-size: 13.5px; color: var(--primary-ink); line-height: 1.6; margin-top: 9px; }
.step { display: flex; align-items: center; gap: 13px; padding: 16px; border-radius: 16px; }
.step-n {
  width: 26px; height: 26px; border-radius: 99px; background: var(--tint); flex-shrink: 0;
  display: grid; place-items: center; font-size: 12px; font-weight: 700; color: var(--primary);
}
.step-txt { font-size: 13px; color: var(--ink-2); line-height: 1.5; }
</style>
