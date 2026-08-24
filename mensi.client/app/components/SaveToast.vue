<script setup lang="ts">
const store = useAppStore()
</script>

<template>
  <Transition name="toast">
    <div v-if="store.toastVisible" class="wrap">
      <div class="toast">
        <div class="row">
          <div class="mark">✓</div>
          <span class="text">Mentve</span>
          <button class="undo" @click="store.undo()">Visszavonás</button>
        </div>
        <div class="bar-track"><div class="bar" /></div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.wrap { position: fixed; left: 16px; right: 16px; bottom: 96px; z-index: 60; display: flex; justify-content: center; }
.toast { width: 100%; max-width: 420px; background: var(--primary); border-radius: 16px; color: #fff; overflow: hidden; box-shadow: 0 10px 34px rgba(90,92,214,.38); }
.row { display: flex; align-items: center; gap: 12px; padding: 14px 16px; }
.mark { width: 26px; height: 26px; border-radius: 99px; background: rgba(255,255,255,.22); display: grid; place-items: center; font-size: 13px; font-weight: 700; flex-shrink: 0; }
.text { font-size: 13.5px; font-weight: 600; }
.undo {
  margin-left: auto; font: 700 13px 'Montserrat', sans-serif; color: #fff;
  background: rgba(255,255,255,.18); border: 0; border-radius: 99px; padding: 7px 13px; cursor: pointer;
}
.undo:hover { background: rgba(255,255,255,.28); }
.bar-track { height: 3px; background: rgba(255,255,255,.25); }
.bar { height: 100%; background: #fff; animation: shrink 3.4s linear forwards; }
@keyframes shrink { from { width: 100%; } to { width: 0; } }
.toast-enter-active, .toast-leave-active { transition: opacity .15s, transform .15s; }
.toast-enter-from, .toast-leave-to { opacity: 0; transform: translateY(8px); }
</style>
