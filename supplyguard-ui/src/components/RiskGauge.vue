<script setup lang="ts">
import { computed } from 'vue'
import { formatRiskPercent } from '../lib/features'

// value: model probability of the risk class (0..1), shown as a semicircle gauge.
const props = defineProps<{ value: number }>()

const clamped = computed(() => Math.min(1, Math.max(0, props.value)))
const level = computed(() => (clamped.value >= 0.66 ? 'high' : clamped.value >= 0.33 ? 'medium' : 'low'))
const label = computed(() => ({ high: 'Yüksek risk', medium: 'Orta risk', low: 'Düşük risk' })[level.value])
const percent = computed(() => formatRiskPercent(clamped.value))

const arc = computed(() => {
  const angle = Math.PI * (1 - clamped.value)
  const x = 100 + 80 * Math.cos(angle)
  const y = 100 - 80 * Math.sin(angle)
  return `M 20 100 A 80 80 0 0 1 ${x.toFixed(2)} ${y.toFixed(2)}`
})
</script>

<template>
  <figure class="gauge" :class="level" role="img" :aria-label="`Risk olasılığı ${percent}, ${label}`">
    <svg viewBox="0 0 200 116" aria-hidden="true">
      <path class="track" d="M 20 100 A 80 80 0 0 1 180 100" />
      <path v-if="clamped > 0" class="value" :d="arc" />
      <text x="100" y="88" text-anchor="middle" class="number">{{ percent }}</text>
      <text x="20" y="114" text-anchor="middle" class="scale">0</text>
      <text x="180" y="114" text-anchor="middle" class="scale">100</text>
    </svg>
    <figcaption>{{ label }}</figcaption>
  </figure>
</template>
