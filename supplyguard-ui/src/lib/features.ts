import type { RiskAnalysisFeatures, RiskAnalysisRequestBody } from '../api/types'

export type FeatureKey = keyof RiskAnalysisFeatures

export interface FeatureDefinition {
  key: FeatureKey
  label: string
  unit: string
  hint: string
  /** Plain-language meaning of the parameter, with a worked example, shown under the input. */
  description: string
  /** Form values for rates are entered as percentages and sent to the API as 0..1. */
  percent: boolean
  integer: boolean
  min: number
  max: number
  step: number
  /** Which end of the range raises risk; used only to colour the input chart. */
  riskRises: 'up' | 'down'
}

// Lower bounds match the API validators. Upper bounds are UI sanity limits for the demo form.
export const FEATURES: readonly FeatureDefinition[] = [
  {
    key: 'averageDeliveryDelayDays', label: 'Ortalama teslimat gecikmesi', unit: 'gün', hint: 'Son dönem ortalaması',
    description: 'Siparişlerin söz verilen teslim tarihinden ortalama kaç gün sonra geldiği. 0 = hep zamanında; 2,5 = ortalama iki buçuk gün geç.',
    percent: false, integer: false, min: 0, max: 60, step: 0.1, riskRises: 'up'
  },
  {
    key: 'onTimeDeliveryRate', label: 'Zamanında teslimat oranı', unit: '%', hint: '0–100 arası',
    description: 'Teslimatların yüzde kaçının söz verilen tarihte veya daha önce geldiği. %95 = her 100 teslimatın 95\'i zamanında.',
    percent: true, integer: false, min: 0, max: 100, step: 0.1, riskRises: 'down'
  },
  {
    key: 'qualityDefectRate', label: 'Kalite hata oranı', unit: '%', hint: '0–100 arası',
    description: 'Teslim alınan parçaların yüzde kaçının kalite kontrolde hatalı bulunduğu. %0,5 = her 1.000 parçanın 5\'i hatalı.',
    percent: true, integer: false, min: 0, max: 100, step: 0.01, riskRises: 'up'
  },
  {
    key: 'deliveryDelayTrendPercentage', label: 'Gecikme trendi (30 gün)', unit: '%', hint: 'Negatif değer iyileşme demektir',
    description: 'Son 30 günün ortalama gecikmesinin önceki 30 güne göre yüzde kaç değiştiği. +%20 = gecikmeler artıyor; −%10 = azalıyor.',
    percent: false, integer: false, min: -100, max: 500, step: 0.1, riskRises: 'up'
  },
  {
    key: 'financialStabilityScore', label: 'Finansal istikrar skoru', unit: 'puan', hint: '0–100 arası',
    description: 'Tedarikçinin mali sağlığını özetleyen puan. 100\'e yakın = güçlü finansal yapı; düşük puan = ödeme güçlüğü veya iflas riski.',
    percent: false, integer: false, min: 0, max: 100, step: 1, riskRises: 'down'
  },
  {
    key: 'activeRiskIndicatorsCount', label: 'Aktif risk göstergesi', unit: 'adet', hint: 'Tam sayı',
    description: 'Tedarikçi için şu an açık olan risk sinyallerinin sayısı (ör. uyum sorunu, kalite bulgusu, bölgesel risk). 0 = bilinen aktif risk yok.',
    percent: false, integer: true, min: 0, max: 50, step: 1, riskRises: 'up'
  }
]

/** Values as shown in the form (rates in percent). */
export type FeatureFormValues = Record<FeatureKey, number | null>

export type FeatureErrors = Partial<Record<FeatureKey, string>>

export function validateFeatures(values: FeatureFormValues): FeatureErrors {
  const errors: FeatureErrors = {}
  for (const feature of FEATURES) {
    const value = values[feature.key]
    if (value === null || !Number.isFinite(value)) errors[feature.key] = 'Bu alan zorunludur.'
    else if (value < feature.min || value > feature.max) errors[feature.key] = `${feature.min} ile ${feature.max} arasında olmalıdır.`
    else if (feature.integer && !Number.isInteger(value)) errors[feature.key] = 'Tam sayı olmalıdır.'
  }
  return errors
}

// Rounds away binary floating-point noise from the percent conversion (e.g. 0.1 * 3 / 100).
const round = (value: number, digits: number) => Number(value.toFixed(digits))

/** Converts validated form values to the API body; call only when validateFeatures returned no errors. */
export function toRequestBody(values: FeatureFormValues, correlationId: string): RiskAnalysisRequestBody {
  const read = (key: FeatureKey) => {
    const feature = FEATURES.find((item) => item.key === key)!
    const value = values[key]
    if (value === null) throw new Error(`Missing value for ${key}`)
    return feature.percent ? round(value / 100, 6) : value
  }
  return {
    correlationId,
    averageDeliveryDelayDays: read('averageDeliveryDelayDays'),
    onTimeDeliveryRate: read('onTimeDeliveryRate'),
    qualityDefectRate: read('qualityDefectRate'),
    deliveryDelayTrendPercentage: read('deliveryDelayTrendPercentage'),
    financialStabilityScore: read('financialStabilityScore'),
    activeRiskIndicatorsCount: read('activeRiskIndicatorsCount')
  }
}

/** API features (rates 0..1) back to form units, e.g. for charts. */
export function toFormValue(feature: FeatureDefinition, apiValue: number): number {
  return feature.percent ? round(apiValue * 100, 4) : apiValue
}

/** Turkish percent notation for a 0..1 probability: "%87,3", or ">%99,9" near certainty. */
export function formatRiskPercent(probability: number): string {
  if (probability > 0.999) return '>%99,9'
  return `%${(probability * 100).toLocaleString('tr-TR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })}`
}

/** 0..1 position of a value in its range, oriented so 1 always means "riskier". */
export function riskPosition(feature: FeatureDefinition, formValue: number): number {
  const clamped = Math.min(feature.max, Math.max(feature.min, formValue))
  const position = (clamped - feature.min) / (feature.max - feature.min)
  return feature.riskRises === 'up' ? position : 1 - position
}

export interface Scenario {
  id: string
  label: string
  description: string
  values: Record<FeatureKey, number>
}

// Illustrative inputs only; the actual probability is whatever the registered model returns.
// No "medium" scenario: the current model is nearly binary, so a middle example would be misleading.
export const SCENARIOS: readonly Scenario[] = [
  {
    id: 'high',
    label: 'Yüksek riskli tedarikçi',
    description: 'Sık gecikme, düşük finansal istikrar',
    values: { averageDeliveryDelayDays: 7.5, onTimeDeliveryRate: 58, qualityDefectRate: 4.5, deliveryDelayTrendPercentage: 40, financialStabilityScore: 32, activeRiskIndicatorsCount: 6 }
  },
  {
    id: 'low',
    label: 'Düşük riskli tedarikçi',
    description: 'İstikrarlı ve zamanında teslimat',
    values: { averageDeliveryDelayDays: 0.3, onTimeDeliveryRate: 98.5, qualityDefectRate: 0.2, deliveryDelayTrendPercentage: -5, financialStabilityScore: 90, activeRiskIndicatorsCount: 0 }
  }
]

export const emptyFormValues = (): FeatureFormValues => ({
  averageDeliveryDelayDays: null,
  onTimeDeliveryRate: null,
  qualityDefectRate: null,
  deliveryDelayTrendPercentage: null,
  financialStabilityScore: null,
  activeRiskIndicatorsCount: null
})
