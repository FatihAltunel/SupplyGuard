import type { ApiErrorItem } from './types'

export class ApiError extends Error {
  readonly status: number
  readonly codes: string[]
  readonly retryAfterSeconds: number | null

  constructor(status: number, message: string, codes: string[] = [], retryAfterSeconds: number | null = null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.codes = codes
    this.retryAfterSeconds = retryAfterSeconds
  }
}

export class NetworkError extends Error {
  constructor() {
    super('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.')
    this.name = 'NetworkError'
  }
}

/** Retry-After is either delay-seconds or an HTTP date (RFC 9110). */
export function parseRetryAfter(value: string | null, now: number = Date.now()): number | null {
  if (!value) return null
  const trimmed = value.trim()
  if (/^\d+$/.test(trimmed)) return Number(trimmed)
  const date = Date.parse(trimmed)
  if (Number.isNaN(date)) return null
  return Math.max(0, Math.ceil((date - now) / 1000))
}

const messages: Record<number, string> = {
  400: 'Gönderilen bilgiler geçersiz.',
  401: 'Oturumunuzun süresi doldu. Lütfen tekrar giriş yapın.',
  403: 'Bu işlem için yetkiniz yok.',
  404: 'Kayıt bulunamadı.',
  409: 'Bu istek kimliği farklı bir analize ait.',
  422: 'Analiz için gereken veriler hesaplanamadı.',
  503: 'Tedarikçi veri kaynakları şu anda kullanılamıyor.'
}

export function describeStatus(status: number, retryAfterSeconds: number | null): string {
  if (status === 429) {
    return retryAfterSeconds
      ? `Çok fazla istek gönderildi. Lütfen ${retryAfterSeconds} saniye sonra tekrar deneyin.`
      : 'Çok fazla istek gönderildi. Lütfen biraz bekleyip tekrar deneyin.'
  }
  return messages[status] ?? `Beklenmeyen bir sunucu hatası oluştu (HTTP ${status}).`
}

async function readErrors(response: Response): Promise<ApiErrorItem[]> {
  try {
    const body: unknown = await response.json()
    return Array.isArray(body)
      ? body.filter((item): item is ApiErrorItem => typeof item?.code === 'string')
      : []
  } catch {
    return []
  }
}

export interface ApiClientOptions {
  fetchImpl?: typeof fetch
  getToken: () => string | null
  onUnauthorized?: () => void
}

export function createApiClient({ fetchImpl = fetch, getToken, onUnauthorized }: ApiClientOptions) {
  async function request<T>(method: 'GET' | 'POST', path: string, body?: unknown): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' }
    const token = getToken()
    if (token) headers.Authorization = `Bearer ${token}`
    if (body !== undefined) headers['Content-Type'] = 'application/json'

    let response: Response
    try {
      response = await fetchImpl(path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body)
      })
    } catch {
      throw new NetworkError()
    }

    if (response.ok) return (await response.json()) as T

    const retryAfter = parseRetryAfter(response.headers.get('Retry-After'))
    const errors = await readErrors(response)
    if (response.status === 401 && token) onUnauthorized?.()
    throw new ApiError(response.status, describeStatus(response.status, retryAfter), errors.map((e) => e.code), retryAfter)
  }

  return {
    get: <T>(path: string) => request<T>('GET', path),
    post: <T>(path: string, body: unknown) => request<T>('POST', path, body)
  }
}

export type ApiClient = ReturnType<typeof createApiClient>
