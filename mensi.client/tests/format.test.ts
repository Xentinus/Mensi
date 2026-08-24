import { describe, expect, it } from 'vitest'
import { addDays, formatDateLong, formatDateShort, formatDelta, formatRange, formatTemp, monthTitle } from '~/utils/format'

describe('format', () => {
  it('temp uses comma and °C', () => {
    expect(formatTemp(36.4)).toBe('36,40 °C')
    expect(formatTemp(null)).toBeNull()
  })
  it('short date is hungarian abbreviation', () => {
    expect(formatDateShort('2026-08-23')).toBe('aug. 23.')
    expect(formatDateShort('2026-03-05')).toBe('márc. 5.')
  })
  it('long date includes weekday', () => {
    expect(formatDateLong('2026-08-23')).toBe('aug. 23., vasárnap')
  })
  it('range collapses within a month and spells across months', () => {
    expect(formatRange('2026-08-23', '2026-08-27')).toBe('aug. 23–27.')
    expect(formatRange('2026-08-30', '2026-09-03')).toBe('aug. 30. – szept. 3.')
  })
  it('delta is signed with comma', () => {
    expect(formatDelta(0.21)).toBe('+0,21')
    expect(formatDelta(-0.06)).toBe('−0,06')
  })
  it('month title', () => {
    expect(monthTitle('2026-08')).toBe('2026. augusztus')
  })
  it('addDays steps back a day without crossing into UTC (regression: used to return 2026-08-22)', () => {
    expect(addDays('2026-08-24', -1)).toBe('2026-08-23')
  })
  it('addDays rolls over the end of a month', () => {
    expect(addDays('2026-08-31', 1)).toBe('2026-09-01')
  })
  it('addDays rolls over the end of a year', () => {
    expect(addDays('2026-01-01', -1)).toBe('2025-12-31')
  })
})
