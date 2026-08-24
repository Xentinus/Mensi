const MONTHS = ['jan.', 'febr.', 'márc.', 'ápr.', 'máj.', 'jún.', 'júl.', 'aug.', 'szept.', 'okt.', 'nov.', 'dec.']
const MONTHS_FULL = ['január', 'február', 'március', 'április', 'május', 'június', 'július',
  'augusztus', 'szeptember', 'október', 'november', 'december']
const WEEKDAYS = ['vasárnap', 'hétfő', 'kedd', 'szerda', 'csütörtök', 'péntek', 'szombat']

const comma = (n: number, digits: number) => n.toFixed(digits).replace('.', ',')

export function formatTemp(value: number | null): string | null {
  return value === null ? null : `${comma(value, 2)} °C`
}

export function formatDateShort(iso: string): string {
  const d = new Date(`${iso}T00:00:00`)
  return `${MONTHS[d.getMonth()]} ${d.getDate()}.`
}

export function formatDateLong(iso: string): string {
  const d = new Date(`${iso}T00:00:00`)
  return `${MONTHS[d.getMonth()]} ${d.getDate()}., ${WEEKDAYS[d.getDay()]}`
}

export function formatRange(fromIso: string, toIso: string): string {
  const from = new Date(`${fromIso}T00:00:00`)
  const to = new Date(`${toIso}T00:00:00`)
  if (from.getMonth() === to.getMonth())
    return `${MONTHS[from.getMonth()]} ${from.getDate()}–${to.getDate()}.`
  return `${formatDateShort(fromIso)} – ${formatDateShort(toIso)}`
}

export function formatDelta(value: number): string {
  const sign = value >= 0 ? '+' : '−'
  return `${sign}${comma(Math.abs(value), 2)}`
}

export function monthTitle(yearMonth: string): string {
  const [year, month] = yearMonth.split('-').map(Number)
  return `${year}. ${MONTHS_FULL[(month ?? 1) - 1]}`
}

export function formatPercent(value: number): string {
  return `${comma(value, value < 10 ? 1 : 0)}%`
}

export function addDays(iso: string, days: number): string {
  const d = new Date(`${iso}T00:00:00`)
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}
