<script setup lang="ts">
const props = defineProps<{ options: number[]; modelValue: number; width?: string }>()
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()
const ROW = 58
const wheel = ref<HTMLElement | null>(null)
let scrollTimer: ReturnType<typeof setTimeout> | null = null

function scrollToValue(behavior: ScrollBehavior = 'auto') {
  const index = props.options.indexOf(props.modelValue)
  if (wheel.value && index >= 0) wheel.value.scrollTo({ top: index * ROW, behavior })
}
onMounted(() => scrollToValue())
watch(() => props.modelValue, () => scrollToValue('smooth'))

function onScroll() {
  if (scrollTimer) clearTimeout(scrollTimer)
  scrollTimer = setTimeout(() => {
    if (!wheel.value) return
    const index = Math.round(wheel.value.scrollTop / ROW)
    const value = props.options[Math.min(Math.max(index, 0), props.options.length - 1)]!
    if (value !== props.modelValue) emit('update:modelValue', value)
  }, 120)
}
</script>

<template>
  <div ref="wheel" class="wheel noscroll" :style="{ width: width ?? '62px' }" @scroll="onScroll">
    <div class="pad" />
    <button v-for="option in options" :key="option" class="item"
      :class="{ active: option === modelValue }" @click="emit('update:modelValue', option)">
      {{ option }}
    </button>
    <div class="pad" />
  </div>
</template>

<style scoped>
.wheel { height: 172px; overflow-y: auto; scroll-snap-type: y mandatory; background: var(--bg); border-radius: 18px; }
.pad { height: 57px; }
.item {
  width: 100%; height: 58px; scroll-snap-align: center; display: grid; place-items: center;
  font: 500 20px 'Montserrat', sans-serif; color: var(--ink-4); border: 0; background: none; cursor: pointer;
}
.item.active { font-size: 30px; font-weight: 700; color: var(--ink); }
</style>
