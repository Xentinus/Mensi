<script setup lang="ts">
const store = useAppStore()
const overview = computed(() => store.overview)
</script>

<template>
  <div v-if="overview" class="stack">
    <MaEmptyState v-if="overview.isEmpty" />
    <template v-else>
      <div v-if="overview.pregnancyHint" class="card hint-card">{{ overview.pregnancyHint }}</div>
      <MaHeroCard :overview="overview" />
      <MaTimingCard :overview="overview" />
      <MaYesterdayCard :overview="overview" />
      <MaTodayCard :overview="overview" />
    </template>
  </div>
</template>

<style scoped>
.stack { display: flex; flex-direction: column; gap: 14px; }
.hint-card { background: var(--tint); color: var(--primary-ink); font-size: 13px; line-height: 1.55; }
</style>
