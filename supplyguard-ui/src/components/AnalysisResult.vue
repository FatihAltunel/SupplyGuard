<script setup lang="ts">
import { computed } from 'vue'
import type { RiskAnalysisStatus } from '../api/types'
import { summarizeExplanation } from '../lib/explanation'
import ContributionChart from './ContributionChart.vue'
import ModelSteps from './ModelSteps.vue'
import RiskGauge from './RiskGauge.vue'

const props = defineProps<{
  phase: 'idle' | 'submitting' | 'polling' | 'done'
  result: RiskAnalysisStatus | null
  error: string | null
  throttled: string | null
}>()

const completedAt = computed(() =>
  props.result?.completedAtUtc ? new Date(props.result.completedAtUtc).toLocaleString('tr-TR') : null)

// Null when the worker sent no SHAP decomposition (failed or older analyses).
const summary = computed(() =>
  props.result?.status === 'Completed' && props.result.explanation
    ? summarizeExplanation(props.result.explanation, props.result.features)
    : null)
</script>

<template>
  <section class="card result" aria-live="polite">
    <h2>Analiz sonucu</h2>

    <p v-if="phase === 'idle' && !error" class="muted empty">
      Bir senaryo seçin veya parametreleri girip analizi başlatın.
    </p>

    <div v-if="phase === 'submitting' || phase === 'polling'" class="waiting">
      <span class="spinner" aria-hidden="true" />
      <div>
        <strong>{{ phase === 'submitting' ? 'İstek gönderiliyor…' : 'Model sonucu bekleniyor…' }}</strong>
        <p class="muted small">İstek kuyruğa alındı; sonuç hazır olduğunda burada görünecek.</p>
      </div>
    </div>

    <p v-if="throttled" class="alert warn">{{ throttled }}</p>
    <p v-if="error" class="alert error" role="alert">{{ error }}</p>

    <template v-if="result?.status === 'Completed' && result.confidenceScore !== null">
      <RiskGauge :value="result.confidenceScore" />
      <article class="explanation">
        <h3>Model açıklaması (SHAP)</h3>
        <!-- Plain text interpolation only; model output is never rendered as HTML. -->
        <p>{{ result.explanationText }}</p>
        <small v-if="completedAt" class="muted">Tamamlandı: {{ completedAt }}</small>
      </article>
    </template>

    <p v-if="result?.status === 'Failed'" class="alert error" role="alert">
      Analiz tamamlanamadı: {{ result.failureMessage ?? 'Bilinmeyen hata' }}
    </p>

    <template v-if="summary && result?.confidenceScore !== null && result?.confidenceScore !== undefined">
      <ModelSteps :summary="summary" :score="result.confidenceScore" />
      <ContributionChart :rows="summary.rows" />
    </template>

    <p v-if="result" class="muted tiny">İstek kimliği: <code>{{ result.correlationId }}</code></p>
  </section>
</template>
