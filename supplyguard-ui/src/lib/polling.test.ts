import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import type { RiskAnalysisState, RiskAnalysisStatus } from '../api/types'
import { PollAbortedError, PollTimeoutError, abortableSleep, pollUntilDone } from './polling'

const status = (state: RiskAnalysisState): RiskAnalysisStatus => ({
  supplierId: 's', correlationId: 'c', status: state, confidenceScore: state === 'Completed' ? 0.8 : null,
  explanationText: null, failureMessage: null, requestedAtUtc: null, completedAtUtc: null, features: null
})

/** Virtual clock: sleeping advances time instantly. */
function clock() {
  let time = 0
  const sleeps: number[] = []
  return {
    sleeps,
    now: () => time,
    sleep: async (ms: number) => { sleeps.push(ms); time += ms }
  }
}

describe('pollUntilDone', () => {
  it('polls at the interval until a terminal status, then stops', async () => {
    const c = clock()
    const fetchStatus = vi.fn<() => Promise<RiskAnalysisStatus>>()
      .mockResolvedValueOnce(status('Pending'))
      .mockResolvedValueOnce(status('Pending'))
      .mockResolvedValueOnce(status('Completed'))
    const updates: string[] = []

    const result = await pollUntilDone(fetchStatus, { intervalMs: 1500, ...c, onUpdate: (s) => updates.push(s.status) })

    expect(result.status).toBe('Completed')
    expect(fetchStatus).toHaveBeenCalledTimes(3)
    expect(c.sleeps).toEqual([1500, 1500])
    expect(updates).toEqual(['Pending', 'Pending', 'Completed'])
  })

  it('treats Failed as terminal', async () => {
    const c = clock()
    const result = await pollUntilDone(async () => status('Failed'), c)
    expect(result.status).toBe('Failed')
    expect(c.sleeps).toEqual([])
  })

  it('waits for Retry-After on 429 and keeps polling', async () => {
    const c = clock()
    const onThrottled = vi.fn()
    const fetchStatus = vi.fn<() => Promise<RiskAnalysisStatus>>()
      .mockRejectedValueOnce(new ApiError(429, 'slow down', ['RateLimit.Exceeded'], 7))
      .mockResolvedValueOnce(status('Completed'))

    const result = await pollUntilDone(fetchStatus, { intervalMs: 1500, ...c, onThrottled })

    expect(result.status).toBe('Completed')
    expect(onThrottled).toHaveBeenCalledWith(7)
    expect(c.sleeps).toEqual([7000])
  })

  it('stops with a timeout error instead of polling forever', async () => {
    const c = clock()
    const fetchStatus = vi.fn(async () => status('Pending'))
    await expect(pollUntilDone(fetchStatus, { intervalMs: 1000, timeoutMs: 5000, ...c })).rejects.toBeInstanceOf(PollTimeoutError)
    expect(fetchStatus.mock.calls.length).toBeLessThanOrEqual(6)
  })

  it('propagates non-throttling errors', async () => {
    const c = clock()
    await expect(pollUntilDone(async () => { throw new ApiError(404, 'missing') }, c)).rejects.toMatchObject({ status: 404 })
  })

  it('can be aborted', async () => {
    const controller = new AbortController()
    controller.abort()
    await expect(pollUntilDone(async () => status('Pending'), { signal: controller.signal })).rejects.toBeInstanceOf(PollAbortedError)
  })
})

describe('abortableSleep', () => {
  it('rejects when aborted mid-wait', async () => {
    const controller = new AbortController()
    const pending = abortableSleep(60_000, controller.signal)
    controller.abort()
    await expect(pending).rejects.toBeInstanceOf(PollAbortedError)
  })
})
