import { describe, expect, it } from 'vitest'
import type { RiskAnalysisExplanation, RiskAnalysisFeatures } from '../api/types'
import { formatEffect, sigmoid, summarizeExplanation } from './explanation'

const features: RiskAnalysisFeatures = {
  averageDeliveryDelayDays: 7.5, onTimeDeliveryRate: 0.58, qualityDefectRate: 0.045,
  deliveryDelayTrendPercentage: 40, financialStabilityScore: 32, activeRiskIndicatorsCount: 6
}

const explanation: RiskAnalysisExplanation = {
  baseValue: -1,
  contributions: [
    { feature: 'averageDeliveryDelayDays', contribution: 0.5 },
    { feature: 'onTimeDeliveryRate', contribution: -0.25 },
    { feature: 'qualityDefectRate', contribution: 2 },
    { feature: 'deliveryDelayTrendPercentage', contribution: 3 },
    { feature: 'financialStabilityScore', contribution: -1 },
    { feature: 'activeRiskIndicatorsCount', contribution: 0.001 }
  ]
}

describe('summarizeExplanation', () => {
  const summary = summarizeExplanation(explanation, features)

  it('orders factors by absolute effect and labels their direction', () => {
    expect(summary.rows.map((r) => r.key)).toEqual([
      'deliveryDelayTrendPercentage', 'qualityDefectRate', 'financialStabilityScore',
      'averageDeliveryDelayDays', 'onTimeDeliveryRate', 'activeRiskIndicatorsCount'
    ])
    expect(summary.rows.map((r) => r.direction)).toEqual(['up', 'up', 'down', 'up', 'down', 'none'])
  })

  it('scales bars to the largest effect and grades strength by share', () => {
    expect(summary.rows[0]).toMatchObject({ width: 100, strength: 'Güçlü' })
    expect(summary.rows[2]).toMatchObject({ width: 33.3, strength: 'Orta' })
    expect(summary.rows[4]).toMatchObject({ strength: 'Zayıf' })
    expect(summary.rows[5]).toMatchObject({ strength: 'Etkisiz' })
  })

  it('shows the user input in form units next to each factor', () => {
    expect(summary.rows.find((r) => r.key === 'onTimeDeliveryRate')!.value).toBe('%58')
    expect(summary.rows.find((r) => r.key === 'averageDeliveryDelayDays')!.value).toBe('7,5 gün')
  })

  it('reconstructs the score from base value plus contributions', () => {
    expect(summary.baseProbability).toBeCloseTo(sigmoid(-1))
    expect(summary.totalEffect).toBeCloseTo(4.251)
    expect(summary.probability).toBeCloseTo(sigmoid(3.251))
  })

  it('keeps unknown features instead of dropping them', () => {
    const rows = summarizeExplanation({ baseValue: 0, contributions: [{ feature: 'newFeature', contribution: 1 }] }, null).rows
    expect(rows[0]).toMatchObject({ key: 'newFeature', label: 'newFeature', value: '' })
  })
})

describe('formatEffect', () => {
  it('formats signed log-odds in Turkish notation without a negative zero', () => {
    expect(formatEffect(2.14)).toBe('+2,1')
    expect(formatEffect(-0.44)).toBe('−0,4')
    expect(formatEffect(-0.04)).toBe('0,0')
  })
})
