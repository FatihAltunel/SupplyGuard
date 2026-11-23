import type { ApiClient } from './client'
import type {
  PagedResult,
  RiskAnalysisAccepted,
  RiskAnalysisRequestBody,
  RiskAnalysisStatus,
  SupplierListItem,
  TokenResult
} from './types'

/** Shared, limited demo account; the endpoint exists only when the server runs with DemoMode enabled. */
export const demoLogin = (api: ApiClient) => api.post<TokenResult>('/api/auth/demo', {})

export const listSuppliers = (api: ApiClient) =>
  api.get<PagedResult<SupplierListItem>>('/api/suppliers?pageNumber=1&pageSize=100')

export const requestRiskAnalysis = (api: ApiClient, supplierId: string, body: RiskAnalysisRequestBody) =>
  api.post<RiskAnalysisAccepted>(`/api/suppliers/${encodeURIComponent(supplierId)}/risk-analyses`, body)

export const getRiskAnalysis = (api: ApiClient, supplierId: string, correlationId: string) =>
  api.get<RiskAnalysisStatus>(
    `/api/suppliers/${encodeURIComponent(supplierId)}/risk-analyses/${encodeURIComponent(correlationId)}`
  )
