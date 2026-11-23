<script setup lang="ts">
import { computed } from 'vue'
import type { RiskAnalysisFeatures } from '../api/types'
import { FEATURES, riskPosition, toFormValue } from '../lib/features'

// Shows the six inputs sent to the model, each placed on its range so that a longer bar is riskier.
// This is an input profile, not the model's SHAP contribution (the API returns only the text explanation).
const props = defineProps<{ features: RiskAnalysisFeatures }>()

const rows = computed(() =>
  FEATURES.map((feature) => {
    const value = toFormValue(feature, props.features[feature.key])
    const position = riskPosition(feature, value)
    return {
      key: feature.key,
      label: feature.label,
      display: `${value.toLocaleString('tr-TR', { maximumFractionDigits: 2 })} ${feature.unit}`,
      width: `${Math.max(2, position * 100).toFixed(1)}%`,
      tone: position >= 0.66 ? 'high' : position >= 0.33 ? 'medium' : 'low'
    }
  })
)
</script>

<template>
  <div class="bars">
    <h3>Girdi profili</h3>
    <p class="muted small">Çubuk uzadıkça ilgili parametre riskli yöne gider.</p>
    <ul>
      <li v-for="row in rows" :key="row.key">
        <div class="bar-label">
          <span>{{ row.label }}</span>
          <strong>{{ row.display }}</strong>
        </div>
        <div class="bar-track"><div class="bar-fill" :class="row.tone" :style="{ width: row.width }" /></div>
      </li>
    </ul>
  </div>
</template>
