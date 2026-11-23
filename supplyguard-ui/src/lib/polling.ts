import { ApiError } from '../api/client'
import type { RiskAnalysisStatus } from '../api/types'

export class PollTimeoutError extends Error {
  constructor() {
    super('Analiz beklenenden uzun sürüyor. Sonuç hazır olduğunda tekrar sorgulayabilirsiniz.')
    this.name = 'PollTimeoutError'
  }
}

export class PollAbortedError extends Error {
  constructor() {
    super('Sorgulama durduruldu.')
    this.name = 'PollAbortedError'
  }
}

export interface PollOptions {
  intervalMs?: number
  timeoutMs?: number
  signal?: AbortSignal
  onUpdate?: (status: RiskAnalysisStatus) => void
  /** Called when the API answers 429; polling then waits for Retry-After before continuing. */
  onThrottled?: (waitSeconds: number) => void
  sleep?: (ms: number, signal?: AbortSignal) => Promise<void>
  now?: () => number
}

export const isTerminal = (status: RiskAnalysisStatus) => status.status === 'Completed' || status.status === 'Failed'

export function abortableSleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) return reject(new PollAbortedError())
    const timer = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    const onAbort = () => {
      clearTimeout(timer)
      reject(new PollAbortedError())
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

/**
 * Polls until the analysis is Completed or Failed. A 429 response is not fatal: polling waits for
 * Retry-After (at least one interval) and continues until the overall timeout. Other errors propagate.
 */
export async function pollUntilDone(
  fetchStatus: () => Promise<RiskAnalysisStatus>,
  {
    intervalMs = 1500,
    timeoutMs = 60_000,
    signal,
    onUpdate,
    onThrottled,
    sleep = abortableSleep,
    now = Date.now
  }: PollOptions = {}
): Promise<RiskAnalysisStatus> {
  const deadline = now() + timeoutMs
  while (true) {
    if (signal?.aborted) throw new PollAbortedError()
    let wait = intervalMs
    try {
      const status = await fetchStatus()
      onUpdate?.(status)
      if (isTerminal(status)) return status
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 429) throw error
      const seconds = error.retryAfterSeconds ?? Math.ceil(intervalMs / 1000)
      onThrottled?.(seconds)
      wait = Math.max(intervalMs, seconds * 1000)
    }
    if (now() + wait > deadline) throw new PollTimeoutError()
    await sleep(wait, signal)
  }
}
