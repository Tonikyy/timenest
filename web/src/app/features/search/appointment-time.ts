const timezone = 'Europe/Helsinki';
const dayFormatter = new Intl.DateTimeFormat('fi-FI', {
  timeZone: timezone,
  weekday: 'short',
  day: 'numeric',
  month: 'numeric',
});
const timeFormatter = new Intl.DateTimeFormat('fi-FI', {
  timeZone: timezone,
  hour: '2-digit',
  minute: '2-digit',
});
const offsetFormatter = new Intl.DateTimeFormat('en-GB', {
  timeZone: timezone,
  timeZoneName: 'longOffset',
});

export const appointmentDay = (timestamp: string) => dayFormatter.format(new Date(timestamp));
export const appointmentTime = (timestamp: string) => timeFormatter.format(new Date(timestamp));

function midnightUtc(year: number, month: number, day: number): string {
  const wallClock = Date.UTC(year, month - 1, day);
  let instant = wallClock;
  // Resolve the offset at local midnight, including daylight-saving boundaries.
  for (let attempt = 0; attempt < 3; attempt++) {
    const offset = offsetFormatter
      .formatToParts(instant)
      .find((part) => part.type === 'timeZoneName')?.value;
    const match = offset?.match(/^GMT([+-])(\d{2}):(\d{2})$/);
    if (!match) throw new Error('Unable to resolve the Helsinki timezone.');
    const minutes = (Number(match[2]) * 60 + Number(match[3])) * (match[1] === '+' ? 1 : -1);
    const resolved = wallClock - minutes * 60_000;
    if (resolved === instant) break;
    instant = resolved;
  }
  return new Date(instant).toISOString();
}

export function helsinkiDayRange(date: string): { from: string; to: string } {
  const match = date.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!match) throw new Error('Invalid calendar date.');
  const [, year, month, day] = match.map(Number);
  const calendar = new Date(Date.UTC(year, month - 1, day));
  if (
    calendar.getUTCFullYear() !== year ||
    calendar.getUTCMonth() !== month - 1 ||
    calendar.getUTCDate() !== day
  ) {
    throw new Error('Invalid calendar date.');
  }
  return { from: midnightUtc(year, month, day), to: midnightUtc(year, month, day + 1) };
}
