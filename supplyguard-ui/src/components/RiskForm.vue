<script setup lang="ts">
import { nextTick, reactive, ref } from 'vue'
import { FEATURES, SCENARIOS, type FeatureErrors, type FeatureFormValues, emptyFormValues, validateFeatures } from '../lib/features'

defineProps<{ busy: boolean; disabled: boolean }>()
const emit = defineEmits<{ submit: [values: FeatureFormValues] }>()

const CUSTOM = 'custom'
const values = reactive<FeatureFormValues>(emptyFormValues())
const errors = ref<FeatureErrors>({})
const activeScenario = ref<string | null>(null)
const firstInput = ref<HTMLInputElement[]>([])

function applyScenario(id: string) {
  const scenario = SCENARIOS.find((s) => s.id === id)
  if (!scenario) return
  Object.assign(values, scenario.values)
  activeScenario.value = id
  errors.value = {}
}

/** Starts from an empty form so the user can describe their own supplier. */
async function enterOwnData() {
  Object.assign(values, emptyFormValues())
  activeScenario.value = CUSTOM
  errors.value = {}
  await nextTick()
  firstInput.value[0]?.focus()
}

function onInput(key: keyof FeatureFormValues, raw: string) {
  values[key] = raw.trim() === '' ? null : Number(raw.replace(',', '.'))
  // Editing a scenario's values turns it into the user's own data.
  activeScenario.value = CUSTOM
}

function submit() {
  errors.value = validateFeatures(values)
  if (Object.keys(errors.value).length === 0) emit('submit', { ...values })
}
</script>

<template>
  <section class="card">
    <h2>Risk parametreleri</h2>
    <p class="muted small form-intro">
      Hazır bir örnek senaryoyla deneyin veya kendi tedarikçinizin değerlerini girin. Her alanın altında ne anlama geldiği açıklanır.
    </p>
    <div class="scenarios" role="group" aria-label="Veri girişi seçenekleri">
      <button
        v-for="s in SCENARIOS"
        :key="s.id"
        type="button"
        class="scenario"
        :class="[s.id, { active: activeScenario === s.id }]"
        :title="s.description"
        :aria-pressed="activeScenario === s.id"
        @click="applyScenario(s.id)"
      >
        {{ s.label }}
      </button>
      <button
        type="button"
        class="scenario custom"
        :class="{ active: activeScenario === CUSTOM }"
        title="Formu temizler; kendi tedarikçi verilerinizi girersiniz"
        :aria-pressed="activeScenario === CUSTOM"
        @click="enterOwnData"
      >
        Kendi verilerinizi girin
      </button>
    </div>

    <form class="fields" novalidate @submit.prevent="submit">
      <div v-for="f in FEATURES" :key="f.key" class="field" :class="{ invalid: errors[f.key] }">
        <label class="field-label" :for="`field-${f.key}`">{{ f.label }} <small>({{ f.unit }})</small></label>
        <input
          :id="`field-${f.key}`"
          ref="firstInput"
          type="number"
          inputmode="decimal"
          :min="f.min"
          :max="f.max"
          :step="f.step"
          :value="values[f.key] ?? ''"
          :aria-invalid="Boolean(errors[f.key])"
          :aria-describedby="`hint-${f.key} desc-${f.key}`"
          @input="onInput(f.key, ($event.target as HTMLInputElement).value)"
        />
        <small :id="`hint-${f.key}`" class="hint">{{ errors[f.key] ?? f.hint }}</small>
        <small :id="`desc-${f.key}`" class="field-desc">{{ f.description }}</small>
      </div>
      <button class="btn btn-primary btn-large wide" type="submit" :disabled="busy || disabled">
        {{ busy ? 'Analiz ediliyor…' : 'Analizi başlat' }}
      </button>
    </form>
  </section>
</template>
