import { describe, expect, it } from 'vitest';
import { appointmentTime, helsinkiDayRange } from './appointment-time';

describe('Helsinki appointment dates', () => {
  it('uses winter time regardless of the browser timezone', () => {
    expect(helsinkiDayRange('2027-01-15')).toEqual({
      from: '2027-01-14T22:00:00.000Z',
      to: '2027-01-15T22:00:00.000Z',
    });
  });
  it('uses summer time', () => {
    expect(helsinkiDayRange('2027-07-15')).toEqual({
      from: '2027-07-14T21:00:00.000Z',
      to: '2027-07-15T21:00:00.000Z',
    });
  });
  it('includes a 23-hour spring transition day', () => {
    const { from, to } = helsinkiDayRange('2027-03-28');
    expect(from).toBe('2027-03-27T22:00:00.000Z');
    expect(to).toBe('2027-03-28T21:00:00.000Z');
  });
  it('includes a 25-hour autumn transition day', () => {
    const { from, to } = helsinkiDayRange('2027-10-31');
    expect(from).toBe('2027-10-30T21:00:00.000Z');
    expect(to).toBe('2027-10-31T22:00:00.000Z');
  });
  it('handles year rollover', () => {
    expect(helsinkiDayRange('2027-12-31').to).toBe('2027-12-31T22:00:00.000Z');
  });
  it('rejects invalid calendar dates', () => {
    expect(() => helsinkiDayRange('2027-02-30')).toThrow();
    expect(() => helsinkiDayRange('invalid')).toThrow();
  });
  it('renders times in Finland', () => {
    expect(appointmentTime('2027-01-15T08:00:00Z')).toBe('10.00');
  });
});
