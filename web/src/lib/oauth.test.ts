import { describe, expect, it } from 'vitest';
import { readOAuthReturn, returnAddress } from './oauth';

describe('readOAuthReturn', () => {
  it('reads the one-time code', () => {
    expect(readOAuthReturn('?code=TEST_CODE')).toEqual({ code: 'TEST_CODE' });
  });

  it('reads an error by its code only', () => {
    expect(readOAuthReturn('?error=access_denied&error_description=The+user+denied')).toEqual({ error: 'access_denied' });
    expect(readOAuthReturn('?error=%3Cscript%3E')).toEqual({ error: 'other' });
  });

  it.each(['', '?', '?state=TEST', `?code=${'x'.repeat(513)}`])('ignores %s', (search) => {
    expect(readOAuthReturn(search)).toBeNull();
  });
});

describe('returnAddress', () => {
  it('drops the query and the route', () => {
    expect(returnAddress({ origin: 'https://example.invalid', pathname: '/SaveHarbor/' })).toBe('https://example.invalid/SaveHarbor/');
  });
});
