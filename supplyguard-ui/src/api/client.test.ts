import { describe, expect, it, vi } from 'vitest'
import { ApiError, NetworkError, createApiClient, describeStatus, parseRetryAfter } from './client'

const json = (status: number, body: unknown, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })

describe('parseRetryAfter', () => {
  it('reads delay-seconds and HTTP dates', () => {
    expect(parseRetryAfter('17')).toBe(17)
    const now = Date.parse('2026-11-23T10:00:00Z')
    expect(parseRetryAfter('Mon, 23 Nov 2026 10:00:30 GMT', now)).toBe(30)
    expect(parseRetryAfter(null)).toBeNull()
    expect(parseRetryAfter('soon')).toBeNull()
  })
})

describe('describeStatus', () => {
  it('explains 429 with the wait time when known', () => {
    expect(describeStatus(429, 12)).toContain('12 saniye')
    expect(describeStatus(429, null)).toContain('Çok fazla istek')
  })
})

describe('createApiClient', () => {
  it('sends the bearer token and JSON body', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockResolvedValue(json(202, { ok: true }))
    const api = createApiClient({ fetchImpl, getToken: () => 'token-1' })

    await expect(api.post('/api/x', { a: 1 })).resolves.toEqual({ ok: true })
    const [, init] = fetchImpl.mock.calls[0]!
    expect((init!.headers as Record<string, string>).Authorization).toBe('Bearer token-1')
    expect(init!.body).toBe('{"a":1}')
  })

  it('turns 429 into an ApiError carrying Retry-After and error codes', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockResolvedValue(
      json(429, [{ code: 'RateLimit.Exceeded', description: 'x' }], { 'Retry-After': '42' }))
    const api = createApiClient({ fetchImpl, getToken: () => 't' })

    const error = await api.get('/api/x').catch((e: unknown) => e)
    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 429, retryAfterSeconds: 42, codes: ['RateLimit.Exceeded'] })
    expect((error as ApiError).message).toContain('42 saniye')
  })

  it('signals an expired session only when a token was sent', async () => {
    const onUnauthorized = vi.fn()
    const fetchImpl = vi.fn<typeof fetch>().mockImplementation(async () => new Response(null, { status: 401 }))
    await createApiClient({ fetchImpl, getToken: () => 't', onUnauthorized }).get('/a').catch(() => undefined)
    await createApiClient({ fetchImpl, getToken: () => null, onUnauthorized }).get('/a').catch(() => undefined)
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('maps fetch failures to a NetworkError', async () => {
    const fetchImpl = vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch'))
    await expect(createApiClient({ fetchImpl, getToken: () => null }).get('/a')).rejects.toBeInstanceOf(NetworkError)
  })
})
