import { problemMessageKey, toProblem } from './problem';

describe('problemMessageKey', () => {
  it('maps known codes to stable keys', () => {
    expect(problemMessageKey('file_not_found')).toBe('errors.fileNotFound');
    expect(problemMessageKey('file_unavailable')).toBe('errors.fileUnavailable');
    expect(problemMessageKey('file_too_large')).toBe('errors.fileTooLarge');
    expect(problemMessageKey('stale_version')).toBe('errors.staleVersion');
    expect(problemMessageKey('legal_hold_active')).toBe('errors.legalHoldActive');
    expect(problemMessageKey('retention_not_allowed')).toBe('errors.retentionNotAllowed');
    expect(problemMessageKey('grant_expired')).toBe('errors.grantInvalid');
    expect(problemMessageKey('forbidden')).toBe('errors.forbidden');
    expect(problemMessageKey('token_expired')).toBe('errors.sessionExpired');
    expect(problemMessageKey('rate_limited')).toBe('errors.rateLimited');
  });

  it('falls back to a generic key for unknown or missing codes', () => {
    expect(problemMessageKey('something-new')).toBe('errors.generic');
    expect(problemMessageKey(undefined)).toBe('errors.generic');
  });
});

describe('toProblem', () => {
  it('reads the code from an Angular HttpErrorResponse-shaped error', async () => {
    const problem = await toProblem({ error: { code: 'file_not_found', detail: 'x' } });
    expect(problem?.code).toBe('file_not_found');
  });

  it('returns null for non-problem payloads', async () => {
    expect(await toProblem({ error: { message: 'boom' } })).toBeNull();
    expect(await toProblem(null)).toBeNull();
    expect(await toProblem(new Error('boom'))).toBeNull();
  });
});
