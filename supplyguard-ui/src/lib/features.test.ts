import { describe, expect, it } from 'vitest'
import { FEATURES, SCENARIOS, emptyFormValues, formatRiskPercent, riskPosition, toFormValue, toRequestBody, validateFeatures } from './features'
import { newCorrelationId } from './correlation'

describe('SCENARIOS', () => {
  it('offers only the high and low risk examples', () => {
    expect(SCENARIOS.map((s) => s.id)).toEqual(['high', 'low'])
  })
})

describe('validateFeatures', () => {
  it('accepts every built-in scenario', () => {
    for (const scenario of SCENARIOS) expect(validateFeatures(scenario.values)).toEqual({})
  })

  it('requires every field and enforces API lower bounds', () => {
    expect(Object.keys(validateFeatures(emptyFormValues()))).toHaveLength(6)
    const values = { ...SCENARIOS[0]!.values, deliveryDelayTrendPercentage: -100.1, onTimeDeliveryRate: 101, activeRiskIndicatorsCount: 1.5 }
    const errors = validateFeatures(values)
    expect(errors.deliveryDelayTrendPercentage).toBeDefined()
    expect(errors.onTimeDeliveryRate).toBeDefined()
    expect(errors.activeRiskIndicatorsCount).toBe('Tam sayı olmalıdır.')
  })
})

describe('toRequestBody', () => {
  it('sends rates as 0..1 and keeps the other features unchanged', () => {
    const body = toRequestBody({ ...SCENARIOS[0]!.values, onTimeDeliveryRate: 58.3, qualityDefectRate: 0.3 }, 'corr-1')
    expect(body).toEqual({
      correlationId: 'corr-1',
      averageDeliveryDelayDays: 7.5,
      onTimeDeliveryRate: 0.583,
      qualityDefectRate: 0.003,
      deliveryDelayTrendPercentage: 40,
      financialStabilityScore: 32,
      activeRiskIndicatorsCount: 6
    })
  })
})

describe('chart helpers', () => {
  const byKey = (key: string) => FEATURES.find((f) => f.key === key)!

  it('formats probabilities in Turkish percent notation', () => {
    expect(formatRiskPercent(0.8731)).toBe('%87,3')
    expect(formatRiskPercent(0)).toBe('%0,0')
    expect(formatRiskPercent(0.9999)).toBe('>%99,9')
  })

  it('converts API rates back to percent', () => {
    expect(toFormValue(byKey('onTimeDeliveryRate'), 0.583)).toBe(58.3)
    expect(toFormValue(byKey('averageDeliveryDelayDays'), 2.4)).toBe(2.4)
  })

  it('orients positions so 1 means riskier', () => {
    expect(riskPosition(byKey('onTimeDeliveryRate'), 100)).toBe(0)
    expect(riskPosition(byKey('averageDeliveryDelayDays'), 60)).toBe(1)
    expect(riskPosition(byKey('averageDeliveryDelayDays'), 999)).toBe(1)
  })
})

describe('newCorrelationId', () => {
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/

  it('falls back to getRandomValues outside secure contexts', () => {
    const insecure = { getRandomValues: globalThis.crypto.getRandomValues.bind(globalThis.crypto) }
    const id = newCorrelationId(insecure)
    expect(id).toMatch(uuid)
    expect(id.length).toBeLessThanOrEqual(128)
  })

  it('uses randomUUID when available', () => {
    expect(newCorrelationId({ getRandomValues: () => { throw new Error('unused') }, randomUUID: () => 'fixed' } as unknown as Crypto)).toBe('fixed')
  })
})
