import { defineStore } from 'pinia'
import type { DailyLog, LogPatch, Overview } from '~/types/api'
import { mergedUndoPayload, type UndoPayload } from '~/utils/undo'

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
      const saved = await useApi().saveLog(date, patch)
      this.showToast(mergedUndoPayload(date, patch, null, before))
      this.refresh()
      return saved
    },

    async saveIntercourse(date: string, events: { protected: boolean | null }[], before: DailyLog | null) {
      const saved = await useApi().saveIntercourse(date, events)
      this.showToast(mergedUndoPayload(date, null, events, before))
      this.refresh()
      return saved
    },

    /** Mező-patch ÉS/VAGY együttlét mentése EGY körben, EGY (összevont) undo-payloaddal.
     *  A teljes varázsló záró mentése ezt hívja — ha külön-külön showToast-olnánk a kettőt,
     *  a második felülírná az első undo-payloadját, és a Visszavonás csak azt állítaná vissza. */
    async saveDay(
      date: string,
      patch: LogPatch | null,
      events: { protected: boolean | null }[] | null,
      before: DailyLog | null,
    ) {
      if (!patch && !events) return
      if (patch) await useApi().saveLog(date, patch)
      if (events) await useApi().saveIntercourse(date, events)
      this.showToast(mergedUndoPayload(date, patch, events, before))
      this.refresh()
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
