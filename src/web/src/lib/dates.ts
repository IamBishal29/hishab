/** Today's Dhaka date as YYYY-MM-DD (a date input's value and the API's date format). */
export function todayInDhaka(now: Date = new Date()): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Dhaka' }).format(now)
}

/** A calendar day from the API (YYYY-MM-DD, already a Dhaka date), e.g. "24 Nov 2026". */
export function formatDay(day: string, language: string): string {
  return new Intl.DateTimeFormat(language === 'bn' ? 'bn-BD' : 'en-GB', {
    timeZone: 'UTC', // the string has no time: read and print it as the same calendar day
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(new Date(`${day}T00:00:00Z`))
}

/** The API stores UTC; sellers think in Dhaka time. Display only. */
export function formatDhakaDateTime(iso: string, language: string): string {
  return new Intl.DateTimeFormat(language === 'bn' ? 'bn-BD' : 'en-GB', {
    timeZone: 'Asia/Dhaka',
    day: 'numeric',
    month: 'short',
    hour: 'numeric',
    minute: '2-digit',
  }).format(new Date(iso))
}
