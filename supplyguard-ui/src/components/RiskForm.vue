<script setup lang="ts">
import { reactive, ref } from 'vue'
import { FEATURES, SCENARIOS, type FeatureErrors, type FeatureFormValues, emptyFormValues, validateFeatures } from '../lib/features'

defineProps<{ busy: boolean; disabled: boolean }>()
const emit = defineEmits<{ submit: [values: FeatureFormValues] }>()

const values = reactive<FeatureFormValues>(emptyFormValues())
const errors = ref<FeatureErrors>({})
const activeScenario = ref<string | null>(null)

function applyScenario(id: string) {
  const scenario = SCENARIOS.find((s) => s.id === id)
  if (!scenario) return
  Object.assign(values, scenario.values)
  activeScenario.value = id
  errors.value = {}
}

function onInput(key: keyof FeatureFormValues, raw: string) {
  values[key] = raw.trim() === '' ? null : Number(raw.replace(',', '.'))
  activeScenario.value = null
}

function submit() {
  errors.value = validateFeatures(values)
  if (Object.keys(errors.value).length === 0) emit('submit', { ...values })
}
</script>

<template>
  <section class="card">
    <h2>Risk parametreleri</h2>
    <div class="scenarios" role="group" aria-label="Örnek senaryolar">
      <button
        v-for="s in SCENARIOS"
        :key="s.id"
        type="button"
        class="scenario"
        :class="[s.id, { active: activeScenario === s.id }]"
        :title="s.description"
        @click="applyScenario(s.id)"
      >
        {{ s.label }}
      </button>
    </div>

    <form class="fields" novalidate @submit.prevent="submit">
      <label v-for="f in FEATURES" :key="f.key" :class="{ invalid: errors[f.key] }">
        <span class="field-label">{{ f.label }} <small>({{ f.unit }})</small></span>
        <input
          type="number"
          inputmode="decimal"
          :min="f.min"
          :max="f.max"
          :step="f.step"
          :value="values[f.key] ?? ''"
          :aria-invalid="Boolean(errors[f.key])"
          @input="onInput(f.key, ($event.target as HTMLInputElement).value)"
        />
        <small class="hint">{{ errors[f.key] ?? f.hint }}</small>
      </label>
      <button class="btn btn-primary btn-large wide" type="submit" :disabled="busy || disabled">
        {{ busy ? 'Analiz ediliyor…' : 'Analizi başlat' }}
      </button>
    </form>
  </section>
</template>
