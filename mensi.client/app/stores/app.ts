import { defineStore } from 'pinia'
import type { DailyLog, LogPatch, Overview } from '~/types/api'

interface UndoPayload {
  date: string
  patch: LogPatch | null
  events: { protected: boolean | null }[] | null
}

export const useAppStore = defineStore('app', {
  state: () => ({
    overview: null as Overview | null,
    loading: false,
    sheetOpen: false,
    sheetDate: null as string | null,
    sheetStep: 0,
    sheetSingle: false,
    toastVisible: false,
    undoPayload: null as UndoPayload | null,
    toastTimer: null as ReturnType<typeof setTimeout> | null,
    refreshTick: 0, // a nézetek erre figyelnek: mentés után újratöltenek
  }),
  actions: {
    async loadOverview() {
      this.loading = true
      try { this.overview = await useApi().overview() }
      finally { this.loading = false }
    },
    openSheet(date: string, step = 0, single = false) {
      this.sheetDate = date
      this.sheetStep = step
      this.sheetSingle = single
      this.sheetOpen = true
    },
    closeSheet() { this.sheetOpen = false },

    /** Mentés + undo-payload építés: a patch kulcsaihoz a mentés ELŐTTI értékek. */
    async saveLog(date: string, patch: LogPatch, before: DailyLog | null) {
      const inverse: LogPatch = {}
      for (const key of Object.keys(patch) as (keyof LogPatch)[]) {
        // @ts-expect-error kulcsonként azonos típus a két oldalon
        inverse[key] = before ? (before[key === 'moods' ? 'moods' : key] ?? null) : null
      }
      if ('periodStart' in patch) inverse.periodStart = before?.periodStart ?? false
      const saved = await useApi().saveLog(date, patch)
      this.showToast({ date, patch: inverse, events: null })
      this.refresh()
      return saved
    },

    async saveIntercourse(date: string, events: { protected: boolean | null }[], before: DailyLog | null) {
      const saved = await useApi().saveIntercourse(date, events)
      this.showToast({ date, patch: null, events: eventsOf(before) })
      this.refresh()
      return saved
    },

    async undo() {
      const payload = this.undoPayload
      if (!payload) return
      this.hideToast()
      if (payload.patch) await useApi().saveLog(payload.date, payload.patch)
      if (payload.events) await useApi().saveIntercourse(payload.date, payload.events)
      this.refresh()
    },

    showToast(payload: UndoPayload) {
      if (this.toastTimer) clearTimeout(this.toastTimer)
      this.undoPayload = payload
      this.toastVisible = true
      this.toastTimer = setTimeout(() => this.hideToast(), 3400)
    },
    hideToast() {
      this.toastVisible = false
      this.undoPayload = null
      if (this.toastTimer) clearTimeout(this.toastTimer)
    },
    refresh() {
      this.refreshTick++
      void this.loadOverview()
    },
  },
})
