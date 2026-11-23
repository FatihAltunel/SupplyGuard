<script setup lang="ts">
import { MODEL_INFO } from '../lib/modelInfo'

// compact: a single-line banner for the dashboard; otherwise the full card for the landing page.
defineProps<{ compact?: boolean }>()
</script>

<template>
  <aside v-if="compact" class="notice notice-compact" role="note">
    <span class="notice-icon" aria-hidden="true">!</span>
    <p>
      <strong>Geliştirme aşamasında:</strong> {{ MODEL_INFO.name }} ({{ MODEL_INFO.algorithm }}) kalibre edilmemiştir;
      sonuçlar simüle edilmiş demo verilerine dayanır ve gerçek tedarik kararlarında kullanılmamalıdır.
    </p>
  </aside>

  <aside v-else class="notice" role="note" aria-labelledby="model-notice-title">
    <div class="notice-head">
      <span class="notice-icon" aria-hidden="true">!</span>
      <div>
        <p class="eyebrow">Model durumu</p>
        <h3 id="model-notice-title">Arka planda çalışan model geliştirme aşamasındadır</h3>
      </div>
    </div>
    <dl class="model-facts">
      <div><dt>Model</dt><dd>{{ MODEL_INFO.name }}</dd></div>
      <div><dt>Kayıt</dt><dd>{{ MODEL_INFO.registry }}</dd></div>
      <div><dt>Sürüm</dt><dd>{{ MODEL_INFO.version }}</dd></div>
      <div><dt>Algoritma</dt><dd>{{ MODEL_INFO.algorithm }}</dd></div>
      <div><dt>Açıklama</dt><dd>{{ MODEL_INFO.explainer }}</dd></div>
      <div><dt>Girdi / Çıktı</dt><dd>{{ MODEL_INFO.features }} parametre · {{ MODEL_INFO.output }}</dd></div>
    </dl>
    <ul class="notice-list">
      <li>Olasılıklar henüz kalibre edilmemiştir; sonuçlar çoğunlukla %0'a veya %100'e yakın çıkar ve küçük girdi değişiklikleri sonucu sert biçimde değiştirebilir.</li>
      <li>Teslimat trendi ve finansal skor gibi kaynaklar demo ortamında simüle edilir.</li>
      <li>Sonuçlar bilgilendirme amaçlıdır; gerçek tedarikçi kararlarında kullanılmamalıdır.</li>
    </ul>
  </aside>
</template>
