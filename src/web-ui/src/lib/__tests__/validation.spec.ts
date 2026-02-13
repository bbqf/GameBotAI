import { ApiError } from '../api';
import { parseFromError, parseValidationErrors } from '../validation';

describe('validation parsing', () => {
  it('maps field errors to specific keys', () => {
    const result = parseValidationErrors([{ field: ' steps ', message: 'Missing steps' }]);

    expect(result.byField.steps).toEqual(['Missing steps']);
    expect(result.general).toEqual([]);
  });

  it('maps keyword errors to heuristic fields', () => {
    const result = parseValidationErrors(['TimeoutMs must be positive']);

    expect(result.byField['blocks[].timeoutMs']).toEqual(['TimeoutMs must be positive']);
    expect(result.general).toEqual([]);
  });

  it('stores unmatched errors as general', () => {
    const result = parseValidationErrors(['Unknown validation issue']);

    expect(result.byField).toEqual({});
    expect(result.general).toEqual(['Unknown validation issue']);
  });

  it('parses validation errors from ApiError', () => {
    const err = new ApiError(400, 'Validation error', [{ field: 'blocks[].type', message: 'Type required' }]);
    const result = parseFromError(err);

    expect(result?.byField['blocks[].type']).toEqual(['Type required']);
  });

  it('returns null for non-ApiError inputs', () => {
    expect(parseFromError(new Error('boom'))).toBeNull();
  });
});
