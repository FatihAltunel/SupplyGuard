<script setup lang="ts">
import { computed } from 'vue'
import { formatEffect, type ExplanationSummary } from '../lib/explanation'
import { formatRiskPercent } from '../lib/features'
import { MODEL_INFO } from '../lib/modelInfo'

// Plain-language walk-through of what the model computed for this request, using the real numbers.
const props = defineProps<{ summary: ExplanationSummary; score: number }>()

const strongest = computed(() => props.summary.rows.find((row) => row.direction !== 'none'))
const strongestDown = computed(() => props.summary.rows.find((row) => row.direction === 'down'))
</script>

<template>
  <section class="model-steps" aria-labelledby="model-steps-title">
    <h3 id="model-steps-title">Model arka planda ne yaptı?</h3>
    <ol>
      <li>
        <strong>Verileriniz modele iletildi.</strong>
        Girdiğiniz 6 değer mesaj kuyruğu üzerinden Python model servisine gönderildi.
      </li>
      <li>
        <strong>{{ MODEL_INFO.trees }} karar ağacı sırayla değerlendirdi.</strong>
        {{ MODEL_INFO.algorithm }}, değerlerinizi örnek tedarikçi verilerinden öğrendiği eşiklerle karşılaştırdı
        (ör. “gecikme trendi belirli bir yüzdenin üzerinde mi?”). Her ağaç tahmine küçük bir artı veya eksi ekledi.
      </li>
      <li>
        <strong>Başlangıç noktası: {{ formatRiskPercent(summary.baseProbability) }}.</strong>
        Model, hiçbir bilgi yokken eğitim verisindeki ortalama tedarikçiden yola çıkar.
      </li>
      <li>
        <strong>Sizin değerleriniz tahmini {{ formatEffect(summary.totalEffect) }} birim
          {{ summary.totalEffect >= 0 ? 'yukarı' : 'aşağı' }} itti → {{ formatRiskPercent(score) }}.</strong>
        <template v-if="strongest">
          En büyük etki <em>{{ strongest.label.toLocaleLowerCase('tr-TR') }}</em> parametresinden geldi<template
            v-if="strongestDown && strongestDown.key !== strongest.key">; riski en çok
            <em>{{ strongestDown.label.toLocaleLowerCase('tr-TR') }}</em> düşürdü</template>.
        </template>
      </li>
      <li>
        <strong>SHAP ile gerekçelendirildi.</strong>
        Toplam etki, aşağıdaki grafikte parametrelere paylaştırıldı; açıklama metni de bu paylaşımdan üretildi.
      </li>
    </ol>
    <p class="muted tiny-note">
      Birimler modelin iç ölçeğidir (log-odds). Model henüz kalibre edilmediği için yüzde çoğunlukla uçlarda çıkar;
      asıl bilgi, hangi parametrenin hangi yöne ve ne kadar etki ettiğidir.
    </p>
  </section>
</template>
