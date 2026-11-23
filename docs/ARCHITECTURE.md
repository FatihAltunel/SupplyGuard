# SupplyGuard Mimarisi

> Durum: 2026-11-23 itibarıyla `main` (b1f93ab) kod tabanından çıkarılmıştır.
> Kodda doğrulanamayan veya niyeti açık olmayan noktalar **[belirsiz]** olarak işaretlidir.
> Bu dosya önceki "Sprint 1 Domain" anlatımının yerini alır; domain kuralları §7'de özetlenmiştir.

---

## 1. Servisler ve sorumlulukları

`docker-compose.yml` 8 servis tanımlar:

| Servis | Teknoloji | Sorumluluk | Port (host) |
|---|---|---|---|
| `api` | .NET 8 ASP.NET Core (`src/SupplyGuard.WebAPI`) | JWT kimlik doğrulama, REST endpoint'leri, CQRS komut/sorguları, isteği **Pending audit + Outbox** olarak tek transaction'da kaydetme. Compose'da arka plan servisleri kapalı (`Messaging__RunBackgroundServices=false`). | `127.0.0.1:8080` |
| `background-worker` | .NET 8 Generic Host (`src/SupplyGuard.BackgroundWorker`) | HTTP yok. `OutboxProcessor` (Outbox → RabbitMQ), `RiskAnalysisResponseConsumer` (yeni akış), `RiskAnalysisCompletedConsumer` (eski v1 akış), `EarlyWarningNotificationConsumer`. | — |
| `migrations` | Aynı WebAPI Dockerfile, `migrations` target | Tek seferlik EF Core migration; başarılı bitmeden `api`/`background-worker` başlamaz. | — |
| `ml-worker` | Python 3.13, FastAPI + aio-pika (`supplyguard-ml-worker`) | Başlangıçta MLflow'dan modeli yükler, `supplyguard.risk.requests` kuyruğunu tüketir, LightGBM `predict_proba` + SHAP `TreeExplainer` ile Türkçe açıklama üretir, yanıtı `supplyguard.risk.responses` kuyruğuna yayınlar. `GET /health`. | `127.0.0.1:8000` |
| `mlflow` | `python:3.10-slim` + çalışma anında `pip install mlflow==3.18.0` | Model Registry ve artifact sunucusu. Backend: SQLite (`./mlruns/mlflow.db`), artifact'ler `./mlruns/artifacts`. Worker `models:/SupplyGuard_JIT_Model/latest` yükler. | `5000` (tüm arayüzler) |
| `postgres` | PostgreSQL 16 | Tek kalıcı veri kaynağı: domain (`supplyguard` şeması), Identity (`identity` şeması), Outbox. | `5433` (tüm arayüzler) |
| `rabbitmq` | RabbitMQ 4.3.6-management | API/.NET worker ile Python worker arasındaki tek iletişim kanalı; ayrıca bildirim olayları. vhost `supplyguard`. | `127.0.0.1:5672`, `127.0.0.1:15672` |
| `redis` | Redis 7 alpine | Yalnızca dağıtık önbellek (bkz. §4). | `6379` (tüm arayüzler) |

.NET katmanları (Clean Architecture): `Domain` (bağımlılıksız entity/kurallar) → `Application` (CQRS handler'lar, validator'lar, mesaj sözleşmeleri, cache dekoratörleri) → `Infrastructure` (EF Core/Npgsql, RabbitMQ.Client, Identity/JWT, Outbox) → `WebAPI` / `BackgroundWorker` (host'lar).

**Dış entegrasyonlar:** `ISupplierPerformanceService` (teslimat gecikme trendi) ve `IFinancialIntegrationService` (finansal istikrar skoru). Gerçek adaptör **yok**; varsayılan `UnavailableSupplierIntegrations` → endpoint `503` döner. Simülasyon `DemoMode:Enabled=true` ile açılır. Bu bayrak ortamdan bağımsızdır (Production'da da açılabilir); compose'da `DEMO_MODE` ile ayarlanır ve varsayılanı `true`'dur.

---

## 2. Uçtan uca akış (JIT risk analizi — güncel akış)

```
Kullanıcı ──POST /api/suppliers/{id}/risk-analyses──▶ api
  api: supplier kontrol → trend + stabilite (simüle) → aktif risk göstergesi sayısı (DB)
       → feature doğrulama → TX{ xai_audit_logs (Pending) + outbox_messages } COMMIT
  ◀── 202 Accepted {supplierId, correlationId, requestMessageId}

background-worker / OutboxProcessor (5 sn poll, batch 20)
  → satırı FOR UPDATE SKIP LOCKED ile sahiplen → RabbitMQ publish (default exchange,
    routing key = supplyguard.risk.requests, persistent, mandatory, publisher confirm)
  → confirm gelirse ProcessedAtUtc; hata → üstel backoff (30 sn … 5 dk, jitter)

ml-worker (prefetch=1, tek uvicorn worker, asyncio.Lock ile seri)
  → JSON + Pydantic doğrulama → DataFrame (6 feature, sabit sıra)
  → classifier.predict_proba → ConfidenceScore = P(risk sınıfı = 1)
  → SHAP TreeExplainer (raw/log-odds) → en yüksek 2 pozitif + en düşük 1 negatif katkı
  → Türkçe ExplanationText
  → supplyguard.risk.responses'a publish (confirm) → ancak sonra isteği ACK
    (hata/bozuk istek: IsSuccessful=false yanıtı yayınlanır, sonra ACK;
     yayın başarısızsa istek requeue)

background-worker / RiskAnalysisResponseConsumer
  → payload + AMQP metadata doğrula (bozuksa supplyguard.risk.responses.error'a karantina, ACK)
  → ProcessRiskAnalysisResponseCommandHandler:
      TX{ audit'i CorrelationId ile FOR UPDATE kilitle → supplier eşleşmesi →
          Pending ise Complete()/Fail(); terminal ise aynı yanıt=Duplicate, farklı=Conflict }
  → COMMIT sonrası ACK (DB hatasında oturum kapanır, mesaj yeniden teslim edilir)

Kullanıcı (Vue UI) ──GET /api/suppliers/{id}/risk-analyses/{correlationId} (~1.5 sn polling)──▶ api
  → GetRiskAnalysisQueryHandler: audit'i AsNoTracking okur (supplier eşleşmesi zorunlu)
  ◀── {status: Pending|Completed|Failed, confidenceScore, explanationText, failureMessage, features{6}}
  UI: Completed/Failed gelince polling durur; 429'da Retry-After kadar bekler; ~60 sn sonra zaman aşımı
```

**"DB → kullanıcı" adımı HTTP polling ile yapılır** (SignalR yok). Durum endpoint'i yalnızca okur ve cache kullanmaz. `features` alanı audit'teki `RequestPayload`'dan, yani worker'a gönderilen girdilerin aynısından okunur; eski v1 audit'lerde `null` döner. JIT akışı `RiskAssessment` veya `EarlyWarning` üretmez; dolayısıyla `GET /api/risk-management/suppliers/{id}/current` JIT sonucunu yansıtmaz.

**Demo modu** (`DemoMode:Enabled=true`, ortamdan bağımsız; gerçek veriyle kullanılmaz):
- Arayüzde giriş formu yoktur. "Demo olarak kullan" butonu `POST /api/auth/demo` ile paylaşılan, parolasız `demo` hesabına token alır.
- Bu hesap `DemoUser` rolündedir ve yalnızca tedarikçi okuma, analiz isteme ve analiz okuma yetkisine sahiptir. `DemoUserSeeder` her açılışta fazladan rol, yetki ve parolayı geri alır.
- POST gövdesindeki `deliveryDelayTrendPercentage`, `financialStabilityScore` ve `activeRiskIndicatorsCount` alanları entegrasyondan ve DB'den gelen değerlerin yerine geçer (`JitFeatureOverridePolicy`). Demo modu dışında bu alanlarla gelen istek `400 RiskAnalysis.OverridesNotAllowed` alır.
- RabbitMQ'ya giden düz JSON sözleşmesi değişmez.
- `DemoSupplierSeeder` 3 kurgusal tedarikçi ekler (vergi no `DEMO000000000x`).

### 2.1 Eski v1 akışı (`POST /api/suppliers/{id}/analyze`)

Hâlâ kayıtlı ve çalışır durumda olan paralel bir akış var:
`RiskAnalysisRequestedEvent` (envelope, `risk-analysis.requested.v1`) → topic exchange `supplyguard.events` → `risk-analysis.requested.queue` → (beklenen) worker → `risk-analysis.completed.v1` → `risk-analysis.completed.queue` → `RiskAnalysisCompletedConsumer` → `RiskAssessment` + terminal `XAIAuditLog` + Redis invalidation.

Ancak güncel Docker imajındaki Python worker bu kuyruğu **dinlemiyor**: `contracts.py` / `inference.py` (sabit "dummy-shap" sonucu üreten Phase A kodu) repoda duruyor ama `main.py` tarafından kullanılmıyor ve Dockerfile onları kopyalamıyor. Sonuç: `/analyze` istekleri `202` alır, outbox'tan yayınlanır ve `risk-analysis.requested.queue`'da tüketilmeden birikir. Bu endpoint'in bilinçli olarak mı bırakıldığı yoksa kaldırılmayı mı beklediği **[belirsiz]**. `docs/SPRINT8_PHASE_A_E2E.md` bu eski akışı anlatır ve artık güncel değildir.

### 2.2 Kural tabanlı değerlendirme ve erken uyarı

`POST /api/risk-management/suppliers/{id}/evaluations` → `RiskAssessmentService` (domain, kural tabanlı, ML yok) → `RiskAssessment` + gerekirse `EarlyWarning` → domain event → `EarlyWarningRaisedV1` outbox → `notification.queue` → `EarlyWarningNotificationConsumer`. Consumer kodda yalnızca loglama yapıyor; e-posta/push gibi gerçek bir bildirim kanalı görülmedi **[belirsiz: planlanan kanal]**.

---

## 3. Servisler arası sözleşmeler

### 3.1 HTTP endpoint'leri (`api`)

| Metot & yol | Yetki (policy) | Açıklama |
|---|---|---|
| `POST /api/auth/login` | anonim | Kullanıcı adı/e-posta + parola → access + refresh token |
| `POST /api/auth/demo` | anonim | Yalnızca demo modunda: paylaşılan `demo` hesabına token (kapalıysa `404`). Rate limit: `authentication` |
| `POST /api/auth/refresh` | anonim | Refresh token rotasyonu |
| `POST /api/auth/logout` | JWT | Refresh token iptali |
| `GET /api/suppliers?pageNumber&pageSize` | `SuppliersRead` | Sayfalı liste (cache yok) |
| `GET /api/suppliers/{id}` | `SuppliersRead` | Detay (**Redis cache-aside**, 10 dk) |
| `POST /api/suppliers` | `SuppliersCreate` | Oluştur; `CountryCode+TaxNumber` çakışması `409` |
| `PUT /api/suppliers/{id}` | `SuppliersUpdate` | Güncelle (cache invalidation) |
| `PATCH /api/suppliers/{id}/status` | `SuppliersChangeStatus` | Durum değiştir (cache invalidation) |
| `POST /api/suppliers/{id}/analyze` | `RiskAssessmentsCreate` | **Eski v1** analiz isteği (§2.1) |
| `POST /api/suppliers/{supplierId}/risk-analyses` | `RiskAssessmentsCreate` | **Güncel JIT** analiz isteği, `202`; `400/404/409/422/429/503` hata eşlemeleri. Rate limit: `analysis-submit` |
| `GET /api/suppliers/{supplierId}/risk-analyses/{correlationId}` | `RiskAssessmentsRead` | JIT durum/sonuç (polling); yoksa veya başka tedarikçiye aitse `404`. Rate limit: `analysis-status` |
| `POST /api/risk-management/suppliers/{id}/evaluations` | `RiskAssessmentsCreate` | Kural tabanlı değerlendirme, `201` |
| `GET /api/risk-management/suppliers/{id}/current` | `RiskAssessmentsRead` | Son `RiskAssessment` |
| `GET /api/risk-management/warnings/active?supplierId` | `RiskAssessmentsRead` | Açık erken uyarılar |
| `PATCH /api/risk-management/suppliers/{sid}/warnings/{wid}/acknowledgement` | `EarlyWarningsManage` | Uyarıyı onayla |
| `GET /health` (`ml-worker`, :8000) | — | RabbitMQ consumer + model hazırsa `200`, değilse `503` |

JIT istek gövdesi (bilinmeyen alan reddedilir, sayılar strict):

```json
{ "correlationId": "manual-02-001", "averageDeliveryDelayDays": 2.4,
  "onTimeDeliveryRate": 0.95, "qualityDefectRate": 0.004 }
```

`correlationId` istemci tarafından verilir, ≤128 karakter, global benzersiz. Aynı correlation + aynı içerik → aynı `requestMessageId` ile `202` (idempotent); farklı içerik → `409`.

### 3.2 RabbitMQ topolojisi

| Kuyruk / exchange | Tür | Üretici → Tüketici | Not |
|---|---|---|---|
| `supplyguard.risk.requests` | durable kuyruk, default exchange | Outbox (.NET) → ml-worker | Güncel JIT isteği |
| `supplyguard.risk.responses` | durable kuyruk, default exchange | ml-worker → `RiskAnalysisResponseConsumer` | Güncel JIT yanıtı |
| `supplyguard.risk.responses.error` | durable kuyruk | `RiskAnalysisResponseConsumer` (karantina) | Tüketicisi yok; manuel inceleme |
| `supplyguard.events` | topic exchange | Outbox | v1 olayları + `EarlyWarningRaisedV1` |
| `risk-analysis.requested.queue` | bind `risk-analysis.requested.v1`, DLX → `supplyguard.ml.errors` | Outbox → **(tüketici yok)** | Eski v1 |
| `risk-analysis.completed.queue` | bind `risk-analysis.completed.v1` | (üretici yok) → `RiskAnalysisCompletedConsumer` | Eski v1 |
| `risk-analysis.invalid.queue` / `supplyguard.ml.errors` | DLX hedefi | — | Eski v1 |
| `risk-analysis.completed.error.queue` / `supplyguard.risk-analysis.errors` | karantina | `RiskAnalysisCompletedConsumer` | Eski v1 |
| `notification.queue` | bind `EarlyWarningRaisedV1.TypeName` | Outbox → `EarlyWarningNotificationConsumer` | |
| `notification.error.queue` / `supplyguard.notification.errors` | karantina | | |

Teslimat garantisi her iki yönde **at-least-once**: publisher confirms + `mandatory=true` + persistent mesaj; ACK yalnızca kalıcı yan etki (DB commit veya confirm edilmiş yanıt) sonrasında.

### 3.3 Mesaj şemaları (güncel, düz JSON)

Kaynak: `supplyguard-ml-worker/schemas.py` ⇄ `src/SupplyGuard.Application/Common/Messaging/RiskAnalysis/Worker/V1/*.cs`. Alan adları büyük/küçük harf duyarlı ve karışık stilde (PascalCase + camelCase); her iki taraf da bilinmeyen alanı reddeder.

**İstek** (`RiskAnalysisRequest`, .NET iç tip adı `risk-analysis.worker-requested.v1`):

| Alan | .NET tipi | Python tipi | Kaynak |
|---|---|---|---|
| `CorrelationId` | string (≤128) | str | HTTP istemcisi |
| `SupplierId` | Guid | str | URL |
| `averageDeliveryDelayDays` | decimal ≥0 | float | HTTP |
| `onTimeDeliveryRate` | decimal 0–1 | float | HTTP |
| `qualityDefectRate` | decimal 0–1 | float | HTTP |
| `DeliveryDelayTrend_Percentage` | decimal ≥−100 | float | `ISupplierPerformanceService` (30 gün) |
| `FinancialStabilityScore` | decimal 0–100 | float | `IFinancialIntegrationService` |
| `ActiveRiskIndicatorsCount` | int ≥0 | float | `risk_indicators` sayımı |

`MessageId` ve `OccurredAtUtc` gövdede değil; AMQP `message_id`, `timestamp`, `type`, `correlation_id` property'lerinde ve `outbox_messages` sütunlarında taşınır.

**Yanıt** (`RiskAnalysisResponse`):

| Alan | Kural |
|---|---|
| `CorrelationId` | isteğinkiyle aynı |
| `SupplierId` | Guid olmalı (.NET); Python bozuk istekte `""` gönderebilir → .NET karantinaya alır |
| `ConfidenceScore` | 0–1; başarısızsa `0` |
| `ExplanationText` | başarılıysa boş olamaz; başarısızsa `""` |
| `IsSuccessful` | bool |
| `ErrorMessage` | başarılıysa `null`, başarısızsa zorunlu (≤2000) |

Model adı, sürümü, gecikme ve sayısal SHAP katkıları yanıtta **yer almaz**.

**Eski v1 envelope** (`RiskAnalysisRequestedEvent` / `RiskAnalysisCompletedEvent`): `messageId`, `messageType`, `correlationId` (UUID), `occurredAtUtc` (ms + `Z`), `supplierId`, `input{featureSchemaVersion=1, observedAtUtc, features{}}`; completion'da `model{name,version,isMock}`, `execution{...,latencyMs}`, `result{riskScore 0–100, confidenceScore, explanation{method:"dummy-shap", baseValue, contributions[]}}` veya `error{code,message}`. Python karşılığı `contracts.py`'de, ancak çalışan worker'a bağlı değil.

### 3.4 Veritabanı (PostgreSQL)

Şema `supplyguard`:

| Tablo | İçerik | Önemli kısıtlar |
|---|---|---|
| `suppliers` | Tedarikçi aggregate root, soft delete | `UNIQUE(CountryCode, TaxNumber)` |
| `risk_assessments` | Değerlendirme (kural tabanlı veya v1 model skoru) | idx `(SupplierId, AssessedAtUtc)` |
| `risk_scores` | Kategori skorları | `UNIQUE(RiskAssessmentId, Category)` |
| `risk_indicators` | Gözlemlenen risk sinyalleri | idx `(SupplierId, Category, IsActive)` |
| `early_warnings` | Uyarılar + durum geçişleri | idx `(SupplierId, Status, DetectedAtUtc)` |
| `xai_audit_logs` | Her model çağrısının izi: request/response payload, `ExplanationStatus` (Pending/RuleBased/Completed/Failed), `ConfidenceScore` (4 ondalık), `ExplanationText`, `RequestMessageId`, zaman damgaları | `UNIQUE(CorrelationId)`; append-only (silme yok, yalnızca Pending → terminal geçişi; `DbContext` ve domain zorlar); `ExplanationStatus` concurrency token |
| `outbox_messages` | `Id`(=MessageId), `MessageType`, `Payload` (jsonb), `CorrelationId`, `NextAttemptAtUtc`, `ProcessedAtUtc`, `QuarantinedAtUtc`, `RetryCount`, `LastError` | Kısmi idx: işlenmemiş & karantinada olmayan |

Şema `identity`: `users`, `roles`, `user_roles`, `user_claims`, `user_logins`, `role_claims`, `user_tokens` + refresh token tablosu. İlk yönetici `.env`'den seed edilir.

Migration sırası: `InitialCreate` → `AddIdentitySchema` → `UpdateXAIAuditLogStatus` → `AddTransactionalOutbox` → `EnforceRiskAnalysisIdempotency` → `AddWorkerRiskAnalysisLifecycle`.

---

## 4. Redis'in rolü

**Yalnızca cache.** Durum (state) veya rate limit için kullanılmıyor.

- Kayıt: `AddStackExchangeRedisCache` → `IDistributedCache` (API ve background-worker).
- Okuma: `CachingQueryHandlerDecorator` yalnızca `GetSupplierByIdQuery`'ye uygulanmış. Anahtar `supplyguard:supplier:v1:{supplierId:N}`, mutlak TTL 10 dk, cache-aside.
- Invalidation: `CacheInvalidatingCommandHandlerDecorator` — `UpdateSupplier`, `ChangeSupplierStatus`, `EvaluateSupplierRisk`, (eski) `ProcessRiskAnalysisCompleted` başarılı olunca anahtarı siler.
- **Fail-open:** okuma/yazma/silme hataları loglanır ve yutulur; `abortConnect=false`; Compose'da Redis sağlığı API'yi bloklamaz. Redis düşerse silinemeyen anahtar TTL'e kadar bayat kalabilir.
- JIT yanıt handler'ı cache'e dokunmaz; `SupplierDetailsDto` JIT sonucunu içermediği için şu an bu bir tutarsızlık değil.
- Rate limiting Redis'te değil, API process belleğinde tutulur (yerleşik `AddRateLimiter`, IP başına sabit dakikalık pencere). Birden fazla API instance'ı çalışırsa limitler instance başına uygulanır. Python worker'ın idempotency için kullandığı bir Redis/cache de yok.
- Redis'te parola/ACL yok ve port `6379` tüm arayüzlere açık.

---

## 5. Test durumu

Bu inceleme sırasında çalıştırıldı (2026-11-23, yerel):

| Suite | Sonuç |
|---|---|
| `SupplyGuard.Domain.Tests` | 23/23 geçti |
| `SupplyGuard.Application.Tests` | 31/31 geçti |
| `SupplyGuard.IntegrationTests` | 123 geçti, 4 atlandı |
| `supplyguard-ml-worker/tests` (unittest) | 23/23 geçti |

- Integration testleri broker için mock/double, kalıcılık için çoğunlukla **SQLite** kullanır. PostgreSQL'e özgü davranış (`FOR UPDATE`, `SKIP LOCKED`) yalnızca atlanan canlı testlerde doğrulanıyor (`WorkerRiskAnalysisLiveTests`, `RabbitMqLiveTests`; `SUPPLYGUARD_WORKER_TEST_POSTGRES`, `SUPPLYGUARD_RUN_RABBITMQ_TESTS=1` gerekir).
- Python testleri harici servis gerektirmez; gerçek LightGBM/SHAP tutarlılığı, ACK/confirm sırası, requeue, health ve lifecycle'ı kapsar.
- **Eksik:** .NET ⇄ Python arasında otomatik uçtan uca test yok (şemalar iki dilde elle eşleştiriliyor; ortak bir sözleşme testi/fixture yok). CI yapılandırması (`.github/` vb.) yok. `contracts.py`/`inference.py` için test yok. Model kalitesi/eğitim için test veya kod yok.

---

## 6. Eksikler, riskli ve belirsiz noktalar

### Fonksiyonel eksikler
1. ~~**Sonuç kullanıcıya ulaşmıyor.**~~ Durum endpoint'i ve polling yapan Vue UI ile kapatıldı (§2). Push bildirim (SignalR) bilinçli olarak yok.
2. **Gerçek veri adaptörleri yok.** Trend ve finansal skor yalnızca Development'ta simüle; diğer ortamlarda endpoint daima `503`. Gerçek kaynakların ne olacağı **[belirsiz]**.
3. **JIT sonucu domain'e yansımıyor.** `RiskAssessment`/`EarlyWarning` üretilmiyor; yüksek risk olasılığı uyarı tetiklemiyor. Kasıtlı mı **[belirsiz]**.
4. **Eski v1 akışı yarım.** `/analyze` istekleri tüketicisiz kuyrukta birikir (§2.1); `RiskAnalysisCompletedConsumer` hiç gelmeyecek mesajı bekler. Kaldırılacak mı, korunacak mı **[belirsiz]**.

### Model / MLflow
5. **Model tekrar üretilemez.** Eğitim kodu repoda yok; `lgbm_jit_v1.0.joblib` ve `mlruns/` `.gitignore`'da. Temiz klonda `register_model.py` çalıştırılamaz → ml-worker başlangıçta model yükleyemez ve `restart: unless-stopped` ile döngüye girer.
6. **`latest` sürümüne bağlılık.** Worker sürüm sabitlemiyor ve kullanılan sürümü yanıtta bildirmiyor; audit'te `ModelVersion = "unreported"`, `LatencyMs = 0`. Hangi sonucun hangi modelden geldiği izlenemiyor.
7. **SHAP katkıları kalıcı değil.** Yalnızca Türkçe metin saklanıyor; sayısal katkılar ve base value kayboluyor. Katkılar log-odds ölçeğinde (README de uyarıyor).
8. **`ConfidenceScore` adlandırması yanıltıcı.** Değer P(risk=1) olasılığı, güven ölçüsü değil. Karar eşiği (ör. hangi olasılık "yüksek risk") tanımlı değil **[belirsiz]**.
9. **MLflow altyapısı kırılgan.** Her açılışta `pip install` (ağ bağımlılığı, yavaş başlangıç), SQLite backend, healthcheck yok; ml-worker yalnızca `service_started` bekliyor. Port 5000 tüm arayüzlere açık, kimlik doğrulama yok.
10. `scikit-learn==1.6.1` eğitim sürümüne sabitli, Python imajı 3.13, MLflow imajı 3.10: sürüm uyumu yalnızca yerelde doğrulanmış görünüyor.

### Mesajlaşma / dayanıklılık
11. **Python tarafında idempotency yok.** Redelivery'de inference tekrar çalışır; .NET tarafı duplicate'i yakalar. Model nondeterministik değilse sorun değil; `latest` değişirse ikinci yanıt "ConflictingResponse" olarak karantinaya düşebilir.
12. **Karantina kuyruklarının tüketicisi/alarmı yok** (`*.error`, `risk-analysis.invalid.queue`). İzleme/yeniden işleme süreci **[belirsiz]**.
13. **Outbox süresiz retry.** Publish hatasında karantina yok, yalnızca backoff (≤5 dk). Uzun süre Pending kalan audit'ler için timeout/temizlik mekanizması yok.
14. **Outbox publish sırasında DB transaction + satır kilidi açık** (publish timeout 30 sn). Broker yavaşsa bağlantı havuzu baskısı oluşabilir.
15. **Throughput sınırı:** ml-worker tek process, `prefetch=1`, `asyncio.Lock` ile seri inference. Yatay ölçekleme (birden çok container) teoride mümkün ama denenmemiş **[belirsiz]**.
16. Compose dışında (`dotnet run`) API'de `Messaging:RunBackgroundServices` varsayılanı `true`; API ve background-worker birlikte çalışırsa ikisi de consumer/outbox çalıştırır. `SKIP LOCKED` çift yayını önler ama niyet **[belirsiz]**.

### Güvenlik / işletim
17. `postgres` (5433), `redis` (6379), `mlflow` (5000) host'ta tüm arayüzlere açık; API/RabbitMQ/ml-worker ise loopback'e bağlı. Redis ve MLflow'da kimlik doğrulama yok.
18. `.env.example` gerçekçi görünen bir JWT anahtarı ve yönetici parolası içeriyor; kopyalanıp değiştirilmeden kullanılma riski var (`<YOUR_...>` placeholder kontrolü yalnızca RabbitMQ parolasını yakalar).
19. Rate limiting yalnızca JIT POST/GET ve login/refresh için var. Eski `/analyze`, `evaluations` ve supplier CRUD endpoint'leri limitsiz. Sayaçlar bellekte tutulur ve API yeniden başlayınca sıfırlanır.
20. `xai_audit_logs.RequestPayload/ResponsePayload` ham iş verisini saklıyor; eski dokümandaki "PII maskeleme" hedefinin uygulanıp uygulanmadığı **[belirsiz]** (JIT payload'larında PII görünmüyor).

### Dokümantasyon
21. `docs/SPRINT8_PHASE_A_E2E.md` ve `docs/supplyguard_sprint_8_zeti.md` eski v1 kuyruklarını anlatıyor; güncel akış için `docs/BACKGROUND_RISK_ANALYSIS.md` ve `docs/JIT_RISK_ANALYSIS.md` geçerli.

---

## 7. Domain modeli (özet)

- `BaseEntity` (`Guid Id`), `AuditableEntity` (oluşturan/değiştiren kullanıcı + UTC zaman), `ISoftDeletable`.
- `Supplier` aggregate root: iş anahtarı `CountryCode + TaxNumber`; `RiskAssessment`, `RiskIndicator`, `EarlyWarning`, `XAIAuditLog` koleksiyonlarını salt okunur tutar ve yalnızca kendi `SupplierId`'sine ait kayıtları kabul eder.
- `RiskAssessment` + `RiskScore`: kategori başına tek skor, ağırlıklı genel skor (0–100); `FromModelScore` ile model skorundan da oluşturulabilir (yalnızca v1 akışı).
- `EarlyWarning`: Open → Acknowledged/Resolved/Dismissed kontrollü geçişleri.
- `XAIAuditLog`: `CreatePending` → tek seferlik `Complete()`/`Fail()`; kimlik ve request alanları değişmez, kayıt silinemez.
- Enum'lar: `SupplierStatus`, `RiskCategory` (7), `RiskLevel` (4), `WarningSeverity` (5), `WarningStatus` (4), `ExplanationStatus` (4).
- Kurallar: private setter, constructor'da invariant kontrolü, EF için private parametresiz constructor, tüm zamanlar UTC `DateTimeOffset`.
