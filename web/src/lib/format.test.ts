import { describe, expect, it } from 'vitest';
import { formatAgo, formatClock, formatSize } from './format';

describe('formatSize', () => {
  it.each([
    [0, '0 B'],
    [512, '512 B'],
    [1536, '1.5 KB'],
    [12345, '12.06 KB'],
    [5 * 1024 * 1024, '5 MB'],
    [3 * 1024 ** 4, '3072 GB'],
    [-1, '0 B']
  ])('%d bytes is %s', (bytes, text) => {
    expect(formatSize(bytes)).toBe(text);
  });
});

describe('formatAgo', () => {
  const now = new Date('2026-10-22T12:00:00Z');

  it.each([
    ['2026-10-22T11:59:30Z', 'just now'],
    ['2026-10-22T12:00:30Z', 'just now'],
    ['2026-10-22T11:55:00Z', '5 min ago'],
    ['2026-10-22T09:00:00Z', '3 h ago'],
    ['2026-10-21T11:00:00Z', 'yesterday'],
    ['2026-10-18T12:00:00Z', '4 days ago']
  ])('%s is %s', (date, text) => {
    expect(formatAgo(new Date(date), now)).toBe(text);
  });
});

describe('formatClock', () => {
  const options = { locale: 'en-GB', timeZone: 'UTC' };
  const now = new Date('2026-10-22T21:00:00Z');

  it('shows only the time today', () => {
    expect(formatClock(new Date('2026-10-22T20:12:00Z'), now, options)).toBe('20:12');
  });

  it('adds the date on another day', () => {
    expect(formatClock(new Date('2026-10-21T20:12:00Z'), now, options)).toBe('21 Oct 20:12');
  });
});
