<script setup lang="ts">
import { ref } from 'vue'
import { ApiError, type ApiClient } from '../api/client'
import { demoLogin } from '../api/endpoints'
import ModelNotice from './ModelNotice.vue'

const props = defineProps<{ api: ApiClient; notice: string | null }>()
const emit = defineEmits<{ signedIn: [token: string] }>()

const busy = ref(false)
const error = ref<string | null>(null)

async function startDemo() {
  busy.value = true
  error.value = null
  try {
    emit('signedIn', (await demoLogin(props.api)).accessToken)
  } catch (e) {
    error.value = e instanceof ApiError && e.status === 404
      ? 'Demo modu bu sunucuda kapalı.'
      : e instanceof Error ? e.message : 'Demo oturumu başlatılamadı.'
  } finally {
    busy.value = false
  }
}

const capabilities = [
  { title: 'Erken uyarı', text: 'Teslimat, kalite ve finansal istikrar sinyallerini tek bir risk olasılığında birleştirir; aksama hatta yansımadan önce görünür olur.' },
  { title: 'Açıklanabilir yapay zekâ', text: 'Her sonuç, riski artıran ve azaltan faktörleri SHAP ile Türkçe olarak gerekçelendirir. Kara kutu karar yoktur.' },
  { title: 'Dayanıklı mimari', text: 'Transactional Outbox, RabbitMQ ve idempotent tüketiciler sayesinde istekler kesintide kaybolmaz, sonuçlar iki kez kaydedilmez.' },
  { title: 'Denetlenebilir kayıt', text: 'Her model çağrısı girdisi, çıktısı ve zaman damgasıyla değiştirilemez bir denetim kaydına yazılır.' }
]

const steps = [
  { title: 'Veri girişi', text: '6 JIT parametresi API\'ye gönderilir; istek ve denetim kaydı aynı işlemde saklanır.', tech: '.NET 8 · PostgreSQL' },
  { title: 'Kuyruk', text: 'Outbox kaydı onaylı teslimatla mesaj kuyruğuna aktarılır.', tech: 'RabbitMQ' },
  { title: 'Model', text: 'Python worker kayıtlı modeli yükler ve risk olasılığını hesaplar.', tech: 'LightGBM · MLflow' },
  { title: 'Açıklama', text: 'Faktör katkıları hesaplanır ve okunabilir bir gerekçeye dönüştürülür.', tech: 'SHAP' },
  { title: 'Sonuç', text: 'Sonuç denetim kaydına işlenir; arayüz hazır olduğunda gösterir.', tech: 'HTTP polling' }
]

const stack = ['.NET 8', 'Python · FastAPI', 'RabbitMQ', 'PostgreSQL', 'Redis', 'MLflow', 'Vue 3', 'Docker']
</script>

<template>
  <section class="hero">
    <div class="shell hero-grid">
      <div class="hero-copy">
        <p class="eyebrow light">JIT üretim için tedarikçi risk analizi</p>
        <h1>Tedarik zinciri aksamalarını hatta ulaşmadan öngörün.</h1>
        <p class="lead">
          SupplyGuard, Just-in-Time çalışan sanayi kuruluşları için tedarikçi kaynaklı aksama riskini erken tespit eder.
          Teslimat, kalite ve finansal sinyalleri tek bir risk skorunda birleştirir ve kararın gerekçesini açıklanabilir
          yapay zekâ ile şeffaf hale getirir.
        </p>
        <div class="hero-actions">
          <button class="btn btn-accent btn-large" type="button" :disabled="busy" @click="startDemo">
            {{ busy ? 'Demo hazırlanıyor…' : 'Demo olarak kullan' }}
          </button>
          <a class="btn btn-ghost-light btn-large" href="#nasil-calisir">Nasıl çalışır?</a>
        </div>
        <p v-if="notice" class="alert warn on-dark" role="status">{{ notice }}</p>
        <p v-if="error" class="alert error on-dark" role="alert">{{ error }}</p>
        <p class="hero-footnote">Kayıt gerekmez. Demo oturumu yalnızca örnek tedarikçiler üzerinde analiz yapabilir.</p>
      </div>

      <div class="hero-panel" aria-hidden="true">
        <div class="panel-head">
          <span>Risk analizi · Canlı akış</span>
          <span class="status-dot">Çalışıyor</span>
        </div>
        <ol class="flow">
          <li><span class="flow-index">01</span><div><strong>İstek alındı</strong><small>202 Accepted · Outbox</small></div></li>
          <li><span class="flow-index">02</span><div><strong>Kuyruğa yazıldı</strong><small>supplyguard.risk.requests</small></div></li>
          <li><span class="flow-index">03</span><div><strong>Model çalıştı</strong><small>LightGBM · SHAP</small></div></li>
          <li class="flow-result">
            <span class="flow-index">04</span>
            <div><strong>Sonuç hazır</strong><small>Risk olasılığı + gerekçe</small></div>
            <span class="pill pill-high">Yüksek risk</span>
          </li>
        </ol>
      </div>
    </div>
  </section>

  <section id="platform" class="section">
    <div class="shell">
      <p class="eyebrow">Platform</p>
      <h2 class="section-title">Görevimiz: tedarik kararlarını ölçülebilir ve açıklanabilir kılmak</h2>
      <p class="section-lead">
        Kritik bir parçanın tek bir gecikmesi bütün üretim hattını durdurabilir. SupplyGuard, satın alma ve tedarik zinciri
        ekiplerine hangi tedarikçinin risk altında olduğunu ve nedenini gösterir.
      </p>
      <div class="cards">
        <article v-for="(item, index) in capabilities" :key="item.title" class="feature-card">
          <span class="feature-index">0{{ index + 1 }}</span>
          <h3>{{ item.title }}</h3>
          <p>{{ item.text }}</p>
        </article>
      </div>
    </div>
  </section>

  <section id="nasil-calisir" class="section section-alt">
    <div class="shell">
      <p class="eyebrow">Nasıl çalışır</p>
      <h2 class="section-title">Olay güdümlü, uçtan uca izlenebilir analiz hattı</h2>
      <ol class="pipeline">
        <li v-for="(step, index) in steps" :key="step.title">
          <span class="pipeline-index">{{ index + 1 }}</span>
          <h3>{{ step.title }}</h3>
          <p>{{ step.text }}</p>
          <small>{{ step.tech }}</small>
        </li>
      </ol>
      <ul class="stack" aria-label="Teknoloji yığını">
        <li v-for="item in stack" :key="item">{{ item }}</li>
      </ul>
    </div>
  </section>

  <section id="model" class="section">
    <div class="shell model-grid">
      <div>
        <p class="eyebrow">Model</p>
        <h2 class="section-title">Şeffaf, sürümlenen ve denetlenebilir model</h2>
        <p class="section-lead">
          Risk modeli MLflow Model Registry'de sürümlenir ve worker başlangıçta kayıtlı modeli yükler. Her tahmin, SHAP
          katkılarıyla hangi faktörün riski artırdığını ve hangisinin azalttığını açıklar.
        </p>
        <button class="btn btn-primary btn-large" type="button" :disabled="busy" @click="startDemo">
          {{ busy ? 'Demo hazırlanıyor…' : 'Demo olarak kullan' }}
        </button>
      </div>
      <ModelNotice />
    </div>
  </section>

  <footer class="site-footer">
    <div class="shell footer-inner">
      <span>© SupplyGuard · Demo ortamı</span>
      <span>Sonuçlar simüle edilmiş veriye dayanır.</span>
    </div>
  </footer>
</template>
