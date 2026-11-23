import type { RiskAnalysisExplanation, RiskAnalysisFeatures } from '../api/types'
import { FEATURES, type FeatureKey, toFormValue } from './features'

export const sigmoid = (logOdds: number) => 1 / (1 + Math.exp(-logOdds))

export interface ContributionRow {
  key: string
  label: string
  /** The user's input in form units, e.g. "7,5 gün"; empty when unknown. */
  value: string
  contribution: number
  direction: 'up' | 'down' | 'none'
  /** Plain-language strength relative to the other factors of this analysis. */
  strength: 'Güçlü' | 'Orta' | 'Zayıf' | 'Etkisiz'
  /** Bar length as a share of the largest absolute contribution (0..100). */
  width: number
}

export interface ExplanationSummary {
  rows: ContributionRow[]
  /** Model starting point (expected value over the training data) as a probability. */
  baseProbability: number
  /** Sum of all contributions in log-odds. */
  totalEffect: number
  /** sigmoid(baseValue + totalEffect): should match the reported score. */
  probability: number
}

const NEGLIGIBLE = 0.01

export function summarizeExplanation(
  explanation: RiskAnalysisExplanation,
  features: RiskAnalysisFeatures | null
): ExplanationSummary {
  const largest = Math.max(...explanation.contributions.map((item) => Math.abs(item.contribution)), NEGLIGIBLE)
  const totalAbs = explanation.contributions.reduce((sum, item) => sum + Math.abs(item.contribution), 0) || 1

  const rows = explanation.contributions
    .map<ContributionRow>((item) => {
      const definition = FEATURES.find((feature) => feature.key === item.feature)
      const raw = definition && features ? features[item.feature as FeatureKey] : undefined
      const magnitude = Math.abs(item.contribution)
      const share = magnitude / totalAbs
      const number = definition && raw !== undefined
        ? toFormValue(definition, raw).toLocaleString('tr-TR', { maximumFractionDigits: 2 })
        : null
      return {
        key: item.feature,
        label: definition?.label ?? item.feature,
        // Turkish notation puts the percent sign first ("%58").
        value: number === null ? '' : definition!.unit === '%' ? `%${number}` : `${number} ${definition!.unit}`,
        contribution: item.contribution,
        direction: magnitude < NEGLIGIBLE ? 'none' : item.contribution > 0 ? 'up' : 'down',
        strength: magnitude < NEGLIGIBLE ? 'Etkisiz' : share >= 0.35 ? 'Güçlü' : share >= 0.12 ? 'Orta' : 'Zayıf',
        width: Math.round((magnitude / largest) * 1000) / 10
      }
    })
    .sort((a, b) => Math.abs(b.contribution) - Math.abs(a.contribution))

  const totalEffect = explanation.contributions.reduce((sum, item) => sum + item.contribution, 0)
  return {
    rows,
    baseProbability: sigmoid(explanation.baseValue),
    totalEffect,
    probability: sigmoid(explanation.baseValue + totalEffect)
  }
}

/** Signed log-odds in Turkish notation, e.g. "+2,1" or "−0,4". */
export function formatEffect(value: number): string {
  const text = Math.abs(value).toLocaleString('tr-TR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
  if (text === '0,0') return text
  return value > 0 ? `+${text}` : `−${text}`
}
