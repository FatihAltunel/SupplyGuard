// Mirrors the .NET WebAPI JSON (camelCase). Keep in sync with
// src/SupplyGuard.WebAPI/Controllers/SupplierRiskAnalysesController.cs and GetRiskAnalysisQuery.cs.

export interface ApiErrorItem {
  code: string
  description: string
}

export interface TokenResult {
  accessToken: string
  accessTokenExpiresAtUtc: string
  refreshToken: string
  refreshTokenExpiresAtUtc: string
}

export interface SupplierListItem {
  id: string
  name: string
  countryCode: string
  taxNumber: string
  isCriticalSupplier: boolean
  city: string | null
  industry: string | null
}

export interface PagedResult<T> {
  items: T[]
  pageNumber: number
  pageSize: number
  totalCount: number
}

/** POST /api/suppliers/{id}/risk-analyses body. The last three fields are demo-mode overrides. */
export interface RiskAnalysisRequestBody {
  correlationId: string
  averageDeliveryDelayDays: number
  onTimeDeliveryRate: number
  qualityDefectRate: number
  deliveryDelayTrendPercentage: number
  financialStabilityScore: number
  activeRiskIndicatorsCount: number
}

export interface RiskAnalysisAccepted {
  supplierId: string
  correlationId: string
  requestMessageId: string
}

export type RiskAnalysisState = 'Pending' | 'Completed' | 'Failed' | 'RuleBased'

export interface RiskAnalysisFeatures {
  averageDeliveryDelayDays: number
  onTimeDeliveryRate: number
  qualityDefectRate: number
  deliveryDelayTrendPercentage: number
  financialStabilityScore: number
  activeRiskIndicatorsCount: number
}

export interface RiskAnalysisStatus {
  supplierId: string
  correlationId: string
  status: RiskAnalysisState
  confidenceScore: number | null
  explanationText: string | null
  failureMessage: string | null
  requestedAtUtc: string | null
  completedAtUtc: string | null
  features: RiskAnalysisFeatures | null
}
