import type { CalendarMonth, Chance, DailyLog, ImportResult, IntercourseEvent, LogPatch, Overview, Trends } from '~/types/api'

export function useApi() {
  return {
    overview: () => $fetch<Overview>('/api/overview'),
    trends: () => $fetch<Trends>('/api/trends'),
    calendar: (year: number, month: number) =>
      $fetch<CalendarMonth>('/api/calendar', { query: { year, month } }),
    chance: () => $fetch<Chance>('/api/chance'),
    logs: (from: string, to: string) =>
      $fetch<{ days: DailyLog[] }>('/api/logs', { query: { from, to } }),
    log: (date: string) => $fetch<DailyLog>(`/api/logs/${date}`),
    saveLog: (date: string, patch: LogPatch) =>
      $fetch<DailyLog>(`/api/logs/${date}`, { method: 'PUT', body: patch }),
    saveIntercourse: (date: string, events: { protected: boolean | null }[]) =>
      $fetch<DailyLog>(`/api/logs/${date}/intercourse`, { method: 'PUT', body: { events } }),
    importPcReport: (file: File, dryRun: boolean, from?: string) => {
      const body = new FormData()
      body.append('file', file)
      return $fetch<ImportResult>('/api/import/pc-report', {
        method: 'POST', query: { dryRun, ...(from ? { from } : {}) }, body,
      })
    },
  }
}

export function eventsOf(log: DailyLog | null): { protected: boolean | null }[] {
  return (log?.intercourse ?? []).map((e: IntercourseEvent) => ({ protected: e.protected }))
}
