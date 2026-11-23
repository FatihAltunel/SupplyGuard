<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { createApiClient } from './api/client'
import { getRiskAnalysis, listSuppliers, requestRiskAnalysis } from './api/endpoints'
import type { RiskAnalysisStatus, SupplierListItem } from './api/types'
import AnalysisResult from './components/AnalysisResult.vue'
import AppHeader from './components/AppHeader.vue'
import LandingPage from './components/LandingPage.vue'
import ModelNotice from './components/ModelNotice.vue'
import RiskForm from './components/RiskForm.vue'
import { newCorrelationId } from './lib/correlation'
import { type FeatureFormValues, toRequestBody } from './lib/features'
import { PollAbortedError, pollUntilDone } from './lib/polling'
import { useSession } from './lib/session'

const session = useSession()
const notice = ref<string | null>(null)
const api = createApiClient({
  getToken: () => session.token.value,
  onUnauthorized: () => {
    stopPolling()
    session.signOut()
    notice.value = 'Demo oturumunun süresi doldu. Devam etmek için yeniden başlatın.'
  }
})

const suppliers = ref<SupplierListItem[]>([])
const supplierId = ref<string>('')
const suppliersError = ref<string | null>(null)

const phase = ref<'idle' | 'submitting' | 'polling' | 'done'>('idle')
const result = ref<RiskAnalysisStatus | null>(null)
const error = ref<string | null>(null)
const throttled = ref<string | null>(null)
let polling: AbortController | null = null

async function loadSuppliers() {
  suppliersError.value = null
  try {
    const page = await listSuppliers(api)
    suppliers.value = page.items
    if (!page.items.some((s) => s.id === supplierId.value)) supplierId.value = page.items[0]?.id ?? ''
  } catch (e) {
    suppliersError.value = e instanceof Error ? e.message : 'Tedarikçiler yüklenemedi.'
  }
}

watch(session.token, (token) => { if (token) void loadSuppliers() }, { immediate: true })

function stopPolling() {
  polling?.abort()
  polling = null
}

async function analyze(values: FeatureFormValues) {
  if (!supplierId.value) return
  stopPolling()
  error.value = null
  throttled.value = null
  result.value = null
  phase.value = 'submitting'
  const target = supplierId.value
  const correlationId = newCorrelationId()
  try {
    await requestRiskAnalysis(api, target, toRequestBody(values, correlationId))
    phase.value = 'polling'
    polling = new AbortController()
    result.value = await pollUntilDone(() => getRiskAnalysis(api, target, correlationId), {
      signal: polling.signal,
      onUpdate: (status) => { result.value = status; throttled.value = null },
      onThrottled: (seconds) => { throttled.value = `Sorgu sınırına ulaşıldı; ${seconds} saniye sonra tekrar denenecek.` }
    })
    phase.value = 'done'
  } catch (e) {
    if (e instanceof PollAbortedError) return
    error.value = e instanceof Error ? e.message : 'Beklenmeyen bir hata oluştu.'
    phase.value = result.value ? 'done' : 'idle'
  } finally {
    throttled.value = null
  }
}

async function signIn(token: string) {
  notice.value = null
  session.signIn(token)
  // Scroll after the dashboard has replaced the (longer) landing page.
  await nextTick()
  window.scrollTo({ top: 0, behavior: 'instant' })
}

function signOut() {
  stopPolling()
  session.signOut()
  result.value = null
  phase.value = 'idle'
}

onBeforeUnmount(stopPolling)
</script>

<template>
  <AppHeader :signed-in="Boolean(session.token.value)" @sign-out="signOut" />

  <LandingPage v-if="!session.token.value" :api="api" :notice="notice" @signed-in="signIn" />

  <main v-else class="dashboard">
    <div class="shell">
      <div class="page-head">
        <div>
          <p class="eyebrow">Risk analizi</p>
          <h1>Tedarikçi risk değerlendirmesi</h1>
          <p class="muted">Bir senaryo seçin veya 6 parametreyi girin; sonuç model tamamladığında otomatik görünür.</p>
        </div>
      </div>

      <ModelNotice compact />

      <section class="card supplier-card">
        <div class="card-head">
          <h2>Tedarikçi</h2>
          <button class="btn btn-ghost btn-small" type="button" @click="loadSuppliers">Yenile</button>
        </div>
        <p v-if="suppliersError" class="alert error">{{ suppliersError }}</p>
        <p v-else-if="!suppliers.length" class="muted">Kayıtlı tedarikçi bulunamadı.</p>
        <select v-else v-model="supplierId" aria-label="Tedarikçi seçin">
          <option v-for="s in suppliers" :key="s.id" :value="s.id">
            {{ s.name }}{{ s.city ? ` · ${s.city}` : '' }}{{ s.isCriticalSupplier ? ' · kritik' : '' }}
          </option>
        </select>
      </section>

      <div class="layout">
        <RiskForm :busy="phase === 'submitting' || phase === 'polling'" :disabled="!supplierId" @submit="analyze" />
        <AnalysisResult :phase="phase" :result="result" :error="error" :throttled="throttled" />
      </div>
    </div>
  </main>
</template>
