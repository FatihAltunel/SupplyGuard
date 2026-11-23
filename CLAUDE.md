# CLAUDE.md — SupplyGuard

## 1. Proje özeti
SupplyGuard bir tedarikçi risk tahmin sistemidir. .NET 8 Web API kullanıcıdan teslimat/kalite metriklerini alır, isteği Outbox üzerinden RabbitMQ'ya bırakır. Python worker, MLflow'dan yüklenen LightGBM modeliyle risk olasılığını hesaplar ve SHAP ile Türkçe bir açıklama üretir. Sonuç PostgreSQL'deki `xai_audit_logs` tablosuna yazılır. Ayrıntılı analiz için bkz. `docs/ARCHITECTURE.md`.

## 2. Mimari
| Servis (compose) | Sorumluluk |
|---|---|
| `api` (`src/SupplyGuard.WebAPI`) | JWT auth, REST, CQRS. JIT isteğini tek transaction'da Pending audit + `outbox_messages` olarak kaydeder, `202` döner. Compose'da arka plan servisleri kapalı (`Messaging__RunBackgroundServices=false`). |
| `background-worker` (`src/SupplyGuard.BackgroundWorker`) | HTTP yok. Outbox → RabbitMQ yayını, yanıt consumer'ı, eski v1 consumer'ı, bildirim consumer'ı. |
| `migrations` | EF Core migration bundle'ı. Tek seferlik çalışır; başarılı bitmeden api ve worker başlamaz. |
| `web` (`supplyguard-ui`) | Vue 3 + Vite build'i, Nginx ile statik sunulur. Tek public giriş noktasıdır (`${WEB_PORT:-80}`); `/api/` isteklerini `api:8080`'e proxy'ler. |
| `ml-worker` (`supplyguard-ml-worker`) | FastAPI + aio-pika. Modeli yükler, `supplyguard.risk.requests` kuyruğunu tüketir, `supplyguard.risk.responses` kuyruğuna yanıt yazar. `GET /health` (:8000). |
| `mlflow` | Model Registry; backend SQLite (`./mlruns`). Model: `models:/SupplyGuard_JIT_Model/latest`. |
| `postgres` / `rabbitmq` / `redis` | Kalıcı veri / mesajlaşma (vhost `supplyguard`) / cache. |

**Uçtan uca akış (güncel JIT):**
1. `POST /api/suppliers/{id}/risk-analyses` isteği gelir.
2. Handler, trend ve finansal skoru entegrasyonlardan alır (yalnızca Development'ta simüle edilir, diğer ortamlarda `503`) ve aktif gösterge sayısını DB'den okur.
3. Tek transaction'da `xai_audit_logs` (Pending) ve `outbox_messages` kaydı açılır, API `202` döner.
4. `OutboxDispatcher` satırı `FOR UPDATE SKIP LOCKED` ile alır ve default exchange'e yayınlar (publisher confirm, mandatory).
5. ml-worker `predict_proba` çalıştırır; `ConfidenceScore` = P(risk=1). SHAP `TreeExplainer` en etkili 2 pozitif ve 1 negatif faktörden Türkçe metin üretir. Yanıt confirm edildikten sonra istek ACK edilir.
6. `RiskAnalysisResponseConsumer` audit'i `FOR UPDATE` ile kilitler ve `Complete()`/`Fail()` çağırır. ACK commit'ten sonra gelir; bozuk yanıtlar `supplyguard.risk.responses.error` kuyruğuna gider.
7. **Kullanıcıya dönüş (HTTP polling):** Vue UI (`web` servisi), `GET /api/suppliers/{id}/risk-analyses/{correlationId}` adresini ~1.5 sn'de bir sorgular ve `Completed` veya `Failed` gelince durur. SignalR kullanılmaz. JIT sonucu `RiskAssessment`/`EarlyWarning` üretmez.

**Demo modu** (`DemoMode:Enabled=true`; ortamdan bağımsız, AWS'de Production'da da açılabilir; compose'da `DEMO_MODE`, varsayılan `true`):
- Simüle entegrasyonlar devreye girer: trend `%15`, finansal skor `85`.
- POST gövdesi `deliveryDelayTrendPercentage`, `financialStabilityScore` ve `activeRiskIndicatorsCount` override alanlarını kabul eder (`JitFeatureOverridePolicy`). Demo modu dışında bu alanlarla gelen istek `400` alır.
- Açılışta `DemoSupplierSeeder` 3 örnek tedarikçi, `DemoUserSeeder` ise parolasız `demo` hesabını ve `DemoUser` rolünü oluşturur. Bu rolün yetkileri: `suppliers.read`, `risk-assessments.read` ve `risk-assessments.create`.
- UI'daki "Demo olarak kullan" butonu `POST /api/auth/demo` çağırır. Endpoint demo modu kapalıysa `404` döner.
- Eski `JitRiskAnalysis:UseSimulatedIntegrations` ayarı kaldırıldı; tanımlıysa API başlarken hata verir.
- Gerçek tedarikçi verisiyle asla açma.

**Rate limiting:** .NET'in yerleşik `AddRateLimiter`'ı kullanılır (`src/SupplyGuard.WebAPI/RateLimiting/`).
- İstemci IP'si başına, dakikalık pencereler: analiz POST, durum GET ve login/refresh/demo girişi için ayrı limitler (`appsettings.json` → `RateLimiting`).
- Limit aşılınca `429` ve `Retry-After` döner.
- `ReverseProxy:TrustForwardedHeaders=true` ise Nginx'in `X-Forwarded-For` başlığı okunur. Compose'da açıktır; API portu yalnızca loopback'e açık kalmalıdır.

Eski v1 akışı (`POST /api/suppliers/{id}/analyze`, `risk-analysis.*.queue`) hâlâ kayıtlı. Ancak güncel Python worker bu kuyrukları dinlemiyor.

**Redis'in rolü: yalnızca cache.** Durum veya rate limit için kullanılmıyor; rate limit sayaçları API process'inin belleğinde tutulur.
- `GetSupplierByIdQuery` cache-aside ile önbelleğe alınır: anahtar `supplyguard:supplier:v1:{id:N}`, TTL 10 dk.
- Supplier update, status değişikliği, evaluate ve eski v1 completion komutları bu anahtarı siler.
- Fail-open: Redis hataları loglanır ve yutulur.

## 3. Dizin yapısı
- `src/SupplyGuard.Domain`: entity'ler, enum'lar, domain event'leri, kural tabanlı `RiskAssessmentService`. Dış bağımlılığı yok.
- `src/SupplyGuard.Application`: `Features/<Alan>/<UseCase>/` (Command/Query + Handler + Validator), `Common/CQRS` (`Result<T>`, `Error`, dekoratörler), `Common/Caching`, `Common/Messaging` (mesaj sözleşmeleri), `Security` (Permissions/Roles).
- `src/SupplyGuard.Infrastructure`: EF Core (`Persistence/`, `Configurations/`, `Migrations/`), Outbox, RabbitMQ (`Messaging/RabbitMq`), JSON serileştirme, Identity/JWT, entegrasyon adaptörleri.
- `src/SupplyGuard.WebAPI`: controller'lar, auth policy'leri, `Program.cs`.
- `src/SupplyGuard.BackgroundWorker`: Generic Host.
- `supplyguard-ml-worker/`: `main.py` (FastAPI lifespan), `rabbitmq.py`, `services.py` (model + SHAP), `schemas.py` (sözleşme), `config.py`, `register_model.py`, `tests/`. `contracts.py` ve `inference.py` eski Phase A kodudur; kullanılmaz ve imaja kopyalanmaz.
- `supplyguard-ui/`: Vue 3 + TypeScript demo arayüzü.
  - `src/api/`: fetch istemcisi, 401/429 eşlemesi, `types.ts`. Bu dosya API JSON'unun aynısıdır.
  - `src/lib/`: polling, form doğrulama, senaryolar, correlation id.
  - `src/components/`: SVG gauge, girdi çubukları, form.
  - `nginx.conf` ve `Dockerfile` (multi-stage: node → nginx).
  - Paketler yalnızca `vue`, `vite`, `@vitejs/plugin-vue`, `typescript`, `vue-tsc`, `vitest`. Yeni paket onay gerektirir.
- `tests/`: `SupplyGuard.Domain.Tests`, `SupplyGuard.Application.Tests`, `SupplyGuard.IntegrationTests` (xUnit, Moq, SQLite).
- `docs/`: mimari ve akış dokümanları. `SPRINT8_PHASE_A_E2E.md` eski v1 akışını anlatır.
- `.claude/commands/precommit.md`: commit öncesi kontrol komutu (`/precommit`).

## 4. Komutlar (PowerShell, depo kökünden)
```powershell
# Ayağa kaldırma (.env gerekir, bkz. .env.example). Config doğrulandı; tam "up" bu incelemede çalıştırılmadı.
docker compose up -d --build
docker compose logs -f api background-worker ml-worker

# .NET build + test (doğrulandı: 23 + 31 + 123 geçti, 4 canlı test atlandı)
dotnet build SupplyGuard.sln
dotnet test SupplyGuard.sln

# Canlı testler (izole DB/broker gerekir; DOĞRULANMADI, çalıştırılmadı)
# Env: SUPPLYGUARD_WORKER_TEST_POSTGRES, RabbitMq__*, SUPPLYGUARD_RUN_RABBITMQ_TESTS=1
dotnet test tests/SupplyGuard.IntegrationTests --filter "FullyQualifiedName~WorkerRiskAnalysisLiveTests|FullyQualifiedName~RabbitMqLiveTests"

# UI test + typecheck + build. Yerelde Node yok; node:22-alpine container'ında doğrulandı (20 Vitest testi geçti).
docker run --rm -v "${PWD}/supplyguard-ui:/app" -w /app node:22-alpine sh -c "npm ci && npm test && npm run build"

# Python test (doğrulandı: 23 geçti). pytest kurulu değil.
cd supplyguard-ml-worker; ..\.venv\Scripts\python.exe -m unittest discover -s tests -v

# Format (dotnet format çalışıyor; main'de şu an 44 WHITESPACE ihlali var)
dotnet format SupplyGuard.sln --verify-no-changes
# Python lint/format: DOĞRULANMADI. ruff kurulu değil, yapılandırma (pyproject/ruff.toml) yok.

# Migration (global dotnet-ef 9.0.0 kurulu, paketler EF 8.0.11; Docker bundle dotnet-ef 8.0.11 kullanır)
# Design-time factory SUPPLYGUARD_CONNECTION_STRING veya ConnectionStrings__SupplyGuardDatabase ister.
dotnet ef migrations add <PascalCaseAd> --project src/SupplyGuard.Infrastructure --startup-project src/SupplyGuard.WebAPI   # DOĞRULANMADI (çalıştırılmadı)
dotnet ef database update --project src/SupplyGuard.Infrastructure --startup-project src/SupplyGuard.WebAPI                # docs'ta yazılı; DOĞRULANMADI

# Modeli MLflow'a kaydetme (gitignore'daki lgbm_jit_v1.0.joblib gerekir; DOĞRULANMADI)
.\.venv\Scripts\python.exe supplyguard-ml-worker/register_model.py
```

## 5. Servisler arası sözleşmeler — iki taraf birlikte güncellenmeli
- **JIT istek/yanıt (güncel):** `supplyguard-ml-worker/schemas.py` ⇄ `src/SupplyGuard.Application/Common/Messaging/RiskAnalysis/Worker/V1/` (`RiskAnalysisRequest.cs`, `RiskAnalysisResponse.cs`, `RiskAnalysisValidators.cs`). Alan adları birebir ve büyük/küçük harf duyarlı (`CorrelationId`, `averageDeliveryDelayDays`, `DeliveryDelayTrend_Percentage` …). İki taraf da bilinmeyen alanı reddeder.
- **Feature sırası:** `schemas.py` içindeki `FEATURE_NAMES` ile modelin `feature_name_` alanı başlangıçta karşılaştırılır.
- **SHAP/açıklama formatı:** `supplyguard-ml-worker/services.py` (`FEATURE_LABELS`, `format_feature`, `risk_shap_values`, `risk_base_value`, `_analyze`). Başarılı yanıt iki şey taşır:
  - `ExplanationText` (Türkçe metin).
  - Sayısal SHAP ayrıştırması: `BaseValue` ve `Contributions[{Feature, Contribution}]`, log-odds biriminde. `BaseValue + Σ Contribution` = risk sınıfının ham skoru.
  - Bu alanlar opsiyoneldir ve yalnızca başarılı yanıtta bulunur. .NET tarafı (`RiskAnalysisResponse.cs`, `RiskAnalysisValidators.cs`) bunları kabul etmeden önce worker güncellenirse yanıtlar karantinaya düşer; deploy sırası: önce `background-worker`, sonra `ml-worker`.
  - Durum endpoint'i bunları `explanation` alanında, `features` ile aynı camelCase anahtarlarla döner. UI tarafı: `supplyguard-ui/src/lib/explanation.ts`.
- **Kuyruk adları/topoloji:** `RabbitMqTopology.cs`, `RabbitMqOptions.cs`, `appsettings.json` (API ve BackgroundWorker), `config.py`, `docker-compose.yml`. Declaration argümanları iki tarafta aynı olmalı.
- **HTTP (UI ⇄ API):** `supplyguard-ui/src/api/types.ts` ⇄ `SupplierRiskAnalysesController.cs` (`RequestSupplierRiskAnalysis`) ve `GetRiskAnalysisQuery.cs` (`RiskAnalysisStatusDto`).
- **Eski v1 envelope:** `Application/Common/Messaging/RiskAnalysis/V1/` ⇄ `contracts.py`.
- **DB:** `Infrastructure/Persistence/Configurations/*` ve `Migrations/`.

## 6. Kodlama kuralları (repoda gözlenen)
- Clean Architecture bağımlılık yönü: Domain ← Application ← Infrastructure ← WebAPI/BackgroundWorker. Domain'e EF, ASP.NET veya mesajlaşma bağımlılığı eklenmez.
- C#: file-scoped namespace, `sealed` sınıflar ve record'lar, primary constructor ile DI, `Nullable` ve `ImplicitUsings` açık. Entity'lerde private setter ve EF için private parametresiz constructor bulunur; invariant'lar constructor ve metotlarda doğrulanır.
- CQRS: `ICommand<T>`/`IQuery<T>` + handler. Validation (FluentValidation) ve cache dekoratörleri `Application/DependencyInjection.cs` içinde elle kaydedilir (MediatR yok).
- Hata yönetimi: iş hataları exception değil, `Result<T>.Failure(new Error("Alan.Kod", "..."))` ile döner (ör. `Supplier.NotFound`, `RiskAnalysis.CorrelationConflict`). Controller'lar kodu HTTP durumuna eşler.
- Mesajlaşma: at-least-once. ACK yalnızca commit veya confirm sonrasında yapılır; kalıcı ret durumunda mesaj önce karantina kuyruğuna yazılır, sonra ACK edilir; idempotency `CorrelationId` ile sağlanır.
- Loglarda ham payload veya secret yazılmaz, yalnızca exception tipi loglanır.
- Zamanlar UTC `DateTimeOffset`; `TimeProvider` enjekte edilir.
- Python: Pydantic `extra="forbid"` ve strict tipler. Inference `asyncio.to_thread` ile event loop dışında çalışır. Kullanıcıya dönük metinler Türkçedir.
- Test adları `Senaryo_Beklenti` biçimindedir (ör. `InvalidHttpPayload_IsRejectedWithoutAuditOrOutbox`).
- Migration adları PascalCase'dir (ör. `AddWorkerRiskAnalysisLifecycle`).

## 7. Yapılmaması gerekenler
- Testsiz davranış değişikliği yapma. Davranış değişiyorsa test ekle veya güncelle.
- Toplu refactor yapma (format düzeltmeleri dahil); istenen kapsamın dışına çıkma.
- Secret, `.env`, connection string, JWT anahtarı veya parola commit'leme. `.env` gitignore'da; değerler User Secrets veya ortam değişkeninden gelir.
- DB şemasını elle değiştirme; yalnızca EF Core migration kullan. `xai_audit_logs` append-only'dir, satır silme veya güncelleme yapma.
- Onaysız NuGet veya pip bağımlılığı ekleme ya da sürüm değiştirme (ör. `scikit-learn==1.6.1` model eğitim sürümüne sabittir).
- `*.joblib`, `mlruns/`, `bin/`, `obj/`, `.venv/` commit'leme.
- Mesaj şemasını tek tarafta değiştirme.

## 8. Git ve GitHub iş akışı
- Repo: FatihAltunel/SupplyGuard. `main` branch'i "protect-main" ruleset'i ile korunuyor: doğrudan push yasak, silme ve force push yasak, her değişiklik Pull Request ile gelir. Required approvals 0 (tek geliştirici), merge'i repo sahibi yapar.
- `main`'e asla doğrudan push etme ve asla force push yapma.
- Her iş için `main`'den yeni branch aç: feature/..., fix/..., refactor/..., docs/...
- Commit mesajları Conventional Commits biçiminde olsun (feat:, fix:, refactor:, test:, docs:, chore:). Değişiklikleri mantıksal parçalara bölerek commit et.
- Commit/push öncesi zorunlu kontrol: testleri ve lint/format'ı çalıştır, git diff'i dosya bazında özetle, secret/.env/build çıktısı/debug kodu olmadığını doğrula, migration, mesaj şeması veya SHAP formatı değişiyorsa ayrıca belirt. Sonra benim onayımı bekle; onay vermeden commit ve push yapma.
- Onaydan sonra branch'i push et ve PR aç. PR açıklaması: ne değişti, neden, nasıl test edildi, riskler.
- PR'ı kendin merge etme; incelemeyi ve merge'i ben yaparım.
- Kapsam dışı değişiklik yapma; gerekiyorsa önce sor.

Commit öncesi kontrol için `/precommit` komutunu kullan.

## Çalışma şekli
- Yeni bir görevde önce plan çıkar ve onay bekle, sonra uygula.
- Emin olmadığın şeyi varsayma, sor.
