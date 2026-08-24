<script setup lang="ts">
const route = useRoute()
const store = useAppStore()
onMounted(() => { void store.loadOverview() })

const NAV = [
  { to: '/', label: 'Ma', icon: 'M12 3.2a8.8 8.8 0 1 1 0 17.6 8.8 8.8 0 0 1 0-17.6Z', icon2: 'M12 7.4V12l3.2 1.9' },
  { to: '/trendek', label: 'Trendek', icon: 'M4 19V6M9 19v-6M14 19V9M19 19v-9', icon2: 'M4 19h16' },
  { to: '/bejegyzesek', label: 'Bejegyzések', icon: 'M6 3.8h9.5L19 7.3V20a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V4.8a1 1 0 0 1 1-1Z', icon2: 'M8.5 12h7M8.5 15.5h7M8.5 8.5h3.5' },
  { to: '/esely', label: 'Esély', icon: 'M12 20s-6.5-4.3-6.5-9A3.9 3.9 0 0 1 12 8.4 3.9 3.9 0 0 1 18.5 11c0 4.7-6.5 9-6.5 9Z', icon2: 'M12 8.4V20' },
]
const TITLES: Record<string, string> = {
  '/': 'Mensi', '/trendek': 'Trendek', '/bejegyzesek': 'Bejegyzések', '/esely': 'Fogamzási esély',
}
const headerTitle = computed(() => TITLES[route.path] ?? 'Mensi')
const headerRight = computed(() => {
  const o = store.overview
  if (!o) return ''
  return route.path === '/' ? formatDateLong(o.today) : o.cycle ? `ciklus ${o.cycle.day}. nap` : ''
})
</script>

<template>
  <div class="shell">
    <aside class="side">
      <div class="brand">
        <div class="brand-badge">M</div>
        <span class="brand-name">Mensi</span>
      </div>
      <nav class="side-nav">
        <NuxtLink v-for="item in NAV" :key="item.to" :to="item.to" class="side-item"
          :class="{ active: route.path === item.to }">
          <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor"
            stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path :d="item.icon" /><path :d="item.icon2" />
          </svg>
          <span>{{ item.label }}</span>
        </NuxtLink>
      </nav>
    </aside>

    <div class="main">
      <header class="topbar">
        <NuxtLink v-if="route.path === '/esely'" to="/" class="back" aria-label="Vissza">←</NuxtLink>
        <span class="topbar-title">{{ headerTitle }}</span>
        <span class="topbar-right">{{ headerRight }}</span>
      </header>

      <main class="content">
        <slot />
      </main>

      <nav class="tabbar">
        <NuxtLink v-for="item in NAV.slice(0, 3)" :key="item.to" :to="item.to" class="tab-item"
          :class="{ active: route.path === item.to }">
          <svg viewBox="0 0 24 24" width="23" height="23" fill="none" stroke="currentColor"
            stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
            <path :d="item.icon" /><path :d="item.icon2" />
          </svg>
          <span>{{ item.label }}</span>
        </NuxtLink>
      </nav>
    </div>

    <div v-if="store.errorMessage" class="error-banner">{{ store.errorMessage }}</div>
    <SaveToast />
    <LogSheet />
  </div>
</template>

<style scoped>
.shell { display: flex; min-height: 100vh; }
.side { display: none; }
.main { flex: 1; min-width: 0; display: flex; flex-direction: column; }
.topbar {
  background: #fff; padding: 14px 16px; display: flex; align-items: center; gap: 11px;
  position: sticky; top: 0; z-index: 5; box-shadow: 0 1px 0 rgba(33, 36, 61, .06);
}
.back {
  width: 30px; height: 30px; border-radius: 10px; background: var(--tint);
  display: grid; place-items: center; font-size: 14px; font-weight: 700;
  color: var(--primary); text-decoration: none;
}
.topbar-title { font-weight: 700; font-size: 16px; letter-spacing: -.01em; }
.topbar-right { margin-left: auto; font-size: 12px; font-weight: 600; color: var(--ink-3); }
.content { flex: 1; padding: 14px 16px 90px; display: flex; flex-direction: column; gap: 14px; max-width: 720px; width: 100%; margin: 0 auto; }
.tabbar {
  background: #fff; position: sticky; bottom: 0; z-index: 5; display: flex;
  box-shadow: 0 -1px 0 rgba(33, 36, 61, .06); padding: 8px 8px 16px;
}
.tab-item {
  flex: 1; padding: 9px 0 7px; border-radius: 16px; display: flex; flex-direction: column;
  align-items: center; gap: 4px; text-decoration: none; color: var(--ink-3);
  font-size: 11px; font-weight: 500;
}
.tab-item.active { color: var(--primary); background: var(--tint); font-weight: 700; }
.error-banner {
  position: fixed; left: 16px; right: 16px; bottom: 150px; margin: 0 auto; max-width: 420px;
  background: #b3261e; color: #fff; border-radius: 12px; padding: 10px 16px;
  font-size: 13px; text-align: center; z-index: 70;
}

@media (min-width: 1000px) {
  .side {
    display: flex; flex-direction: column; width: 224px; flex-shrink: 0; background: #fff;
    position: sticky; top: 0; height: 100vh; box-shadow: 1px 0 0 rgba(33, 36, 61, .06);
  }
  .brand { padding: 22px 20px 18px; display: flex; align-items: center; gap: 11px; }
  .brand-badge {
    width: 30px; height: 30px; background: var(--primary); border-radius: 10px;
    display: grid; place-items: center; color: #fff; font-weight: 700; font-size: 14px;
  }
  .brand-name { font-weight: 700; font-size: 17px; letter-spacing: -.01em; }
  .side-nav { padding: 6px 12px; display: flex; flex-direction: column; gap: 2px; }
  .side-item {
    padding: 11px 14px; border-radius: 12px; display: flex; align-items: center; gap: 11px;
    text-decoration: none; color: var(--ink-2); font-size: 13.5px; font-weight: 500;
  }
  .side-item:hover { background: var(--tint); }
  .side-item.active { background: var(--tint); color: var(--primary); font-weight: 700; }
  .tabbar { display: none; }
  .content { padding-bottom: 24px; }
}
</style>
