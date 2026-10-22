// Display formats. Locale and time zone are parameters so tests are independent of the machine they run on.
export interface FormatOptions {
  locale?: string;
  timeZone?: string;
}

// Same units and rounding as DisplayFormatter.FormatBytes in the desktop app.
export function formatSize(bytes: number): string {
  const units = ['B', 'KB', 'MB', 'GB'];
  let size = Math.max(0, bytes);
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }

  return `${Math.round(size * 100) / 100} ${units[unit]}`;
}

export function formatAgo(date: Date, now: Date): string {
  const minutes = Math.floor((now.getTime() - date.getTime()) / 60_000);
  if (minutes < 1) {
    return 'just now';
  }

  if (minutes < 60) {
    return `${minutes} min ago`;
  }

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    return `${hours} h ago`;
  }

  const days = Math.floor(hours / 24);
  return days === 1 ? 'yesterday' : `${days} days ago`;
}

// The time of day, with the date when it is not today.
export function formatClock(date: Date, now: Date, options: FormatOptions = {}): string {
  const day = new Intl.DateTimeFormat(options.locale, { timeZone: options.timeZone, year: 'numeric', month: 'numeric', day: 'numeric' });
  const time = new Intl.DateTimeFormat(options.locale, { timeZone: options.timeZone, hour: '2-digit', minute: '2-digit' }).format(date);
  if (day.format(date) === day.format(now)) {
    return time;
  }

  const shortDate = new Intl.DateTimeFormat(options.locale, { timeZone: options.timeZone, month: 'short', day: 'numeric' }).format(date);
  return `${shortDate} ${time}`;
}
