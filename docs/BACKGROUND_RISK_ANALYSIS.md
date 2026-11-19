# Python ML worker ile .NET arka plan entegrasyonu

## Çalıştırma

`SupplyGuard.BackgroundWorker` .NET 8 Generic Host kullanır. HTTP portu açmaz.
Outbox publisher, yeni risk yanıt consumer'ı ve mevcut bağımsız eski consumer'lar bu
host'ta çalışır. Compose, API'de `Messaging__RunBackgroundServices=false` ayarlar;
arka plan görevleri ayrı `background-worker` servisinde yürütülür.

Yerel çalıştırmada şu ayarları environment üzerinden sağlayın:

- `ConnectionStrings__SupplyGuardDatabase`
- `ConnectionStrings__Redis`
- `RabbitMq__HostName`, `RabbitMq__Port`, `RabbitMq__VirtualHost`
- `RabbitMq__UserName`, `RabbitMq__Password`, gerekiyorsa `RabbitMq__UseTls`

Önce `AddWorkerRiskAnalysisLifecycle` migration'ını uygulayın. Compose'un mevcut
`migrations` servisi bunu worker başlamadan yapar. Ardından:

```powershell
docker compose up --build -d background-worker
```

`background-worker`, RabbitMQ için yalnızca `service_started` bekler. Bağlantılar
Polly.Core 8.6.5 retry pipeline'ından geçer: ilk deneme, ardından 2, 4 ve 8 saniye
beklemelerle üç tekrar. Denemeler tükenirse consumer'ın yeniden bağlantı döngüsü
veya kalıcı Outbox'ın yeniden gönderim takvimi devreye girer. Host cancellation
token'ı hem bağlantıyı hem beklemeleri iptal eder.

`RabbitMq:ConnectionRetryCount`, `ConnectionRetryDelay`, `ConnectionRetryMaxDelay`
ve `ConnectionRetryUseJitter` ayarlanabilir. Bağlantı/publish timeout'ları toplam
işlem süresini ayrıca sınırlar. Publish işlemi için Polly ile ayrı bir tekrar
yapılmaz; bu sorumluluk Outbox'tadır. Outbox retry jitter'ı worker'da açıktır.

## HTTP endpoint'i ve Postman

API artık `AddJitRiskAnalysis()` kaydını yapar ve şu yetkili endpoint'i sunar:

```http
POST /api/suppliers/{supplierId}/risk-analyses
Authorization: Bearer <accessToken>
Content-Type: application/json
```

```json
{
  "correlationId": "manual-02-001",
  "averageDeliveryDelayDays": 2.4,
  "onTimeDeliveryRate": 0.95,
  "qualityDefectRate": 0.004
}
```

Dört alan da zorunludur; supplier kimliği URL'den alınır. `Features` sarmalı veya
worker'ın diğer üç hesaplanmış özelliği HTTP body içine konmaz. Endpoint
`risk-assessments.create` izni gerektirir. Başarılı kayıt `202 Accepted` döndürür:

```json
{
  "supplierId": "11111111-1111-4111-8111-111111111111",
  "correlationId": "manual-02-001",
  "requestMessageId": "22222222-2222-4222-8222-222222222222"
}
```

Bu kimlikler biçim örneğidir. Yanıt, Pending audit ve Outbox transaction'ının
tamamlandığını belirtir. Python sonucunu beklemez ve doğrudan RabbitMQ yayını
yapmaz. Broker kapalı olsa da PostgreSQL ve veri kaynakları çalışıyorsa istek
kaydedilebilir. Aynı correlation ve aynı snapshot tekrarında aynı kimlikler döner;
farklı içerik `409 Conflict` üretir.

Postman adımları:

1. Collection değişkenlerini `baseUrl=http://localhost:8080`, mevcut bir
   `supplierId`, yeni bir `correlationId` ve boş `accessToken` olarak oluşturun.
   API portu farklıysa `baseUrl` değerini değiştirin.
2. `POST {{baseUrl}}/api/auth/login` isteğini `No Auth` ile gönderin. Body/raw/JSON:
   `{"userNameOrEmail":"<kullanıcı veya e-posta>","password":"<parola>"}`.
   Mevcut hesabın bilgilerini kullanın; ilk admin bilgileri `.env` içindedir.
3. Yanıttaki `accessToken` değerini collection değişkenine kopyalayın. Collection
   Authorization türünü `Bearer Token`, değerini `{{accessToken}}` yapın.
4. `GET {{baseUrl}}/api/suppliers` isteğinden mevcut bir tedarikçinin `id`
   değerini alıp `supplierId` değişkenine yazın.
5. `POST {{baseUrl}}/api/suppliers/{{supplierId}}/risk-analyses` isteğini oluşturun.
   Authorization `Inherit auth from parent`, Body `raw/JSON` olsun. Yukarıdaki
   body'de correlation değerini `{{correlationId}}` ile değiştirip Send seçin.
6. `202` ve istek kimliklerini doğrulayın; audit sonucunu aynı correlation ile
   veritabanından, ilerlemeyi `background-worker` / `ml-worker` loglarından izleyin.

Durum kodları: hatalı/eksik body `400`, token yok/geçersiz `401`, izin eksik `403`,
tedarikçi yok `404`, correlation çakışması `409`, hesaplanamayan trend veya geçersiz
kaynak özellikleri `422`, kullanılamayan performans/finans adaptörü `503`.

Projede gerçek performans/finans adaptörleri henüz yoktur. Yerel test için
`JitRiskAnalysis:UseSimulatedIntegrations=true` açıkça seçilebilir; bu seçenek
yalnızca Development ortamında kabul edilir. Compose'un mevcut Development API
servisinde açıktır. Trend `%15`, finansal istikrar `85` sentetik değerlerdir;
aktif gösterge sayısı veritabanından, ML sonucu gerçek Python worker'dan gelir.
Varsayılan ayar kapalıdır. Üretimde gerçek `ISupplierPerformanceService` ve
`IFinancialIntegrationService` adaptörlerini kaydedin; yoksa endpoint sahte veri
üretmeden `503` döner. Yerelde doğrudan `dotnet run` kullanırken bu seçeneği
environment veya User Secrets üzerinden ayrıca etkinleştirin.

API image'ını güncellemek için `docker compose up -d --build api` kullanın.
Endpoint Swagger'da görünür. Eski `/api/suppliers/{id}/analyze` v1 endpoint'i
ayrı sözleşmesini kullanmaya devam eder.

## Sözleşme ve kuyruklar

Referans Python sözleşmesi `supplyguard-ml-worker/schemas.py` dosyasındadır.
`RiskAnalysisRequest` yalnızca kökteki şu sekiz alanı taşır:

`CorrelationId`, `SupplierId`, `averageDeliveryDelayDays`, `onTimeDeliveryRate`,
`qualityDefectRate`, `DeliveryDelayTrend_Percentage`, `FinancialStabilityScore`,
`ActiveRiskIndicatorsCount`.

`Features` sarmalı bulunmaz. .NET'teki metadata property'leri `JsonIgnore` kullanır;
mesaj kimliği ve zaman Outbox sütunlarında, teslimatta da AMQP property'lerinde
tutulur. İç mesaj tipi `risk-analysis.worker-requested.v1` olarak sürümlenir.

Publisher default exchange'e (`""`), `supplyguard.risk.requests` routing key'iyle
persistent JSON yayınlar. Durable kuyruk, `mandatory=true` ve publisher confirms
kullanılır. Kuyruk declaration argümanları Python ile aynıdır; mevcut kuyruk tipi
veya DLX ayarları değiştirilmez.

Consumer `supplyguard.risk.responses` kuyruğundan şu alanları okur:
`CorrelationId`, `SupplierId`, `ConfidenceScore`, `ExplanationText`,
`IsSuccessful`, isteğe bağlı/null `ErrorMessage`. Python yanıtında `MessageId`
ve `Type` gerekmez. Eksik zorunlu alanlar, yanlış harf büyüklüğü, bilinmeyen veya
tekrarlanan JSON alanları ve geçersiz sayısal değerler reddedilir.

`ConfidenceScore`, risk sınıfının 0–1 olasılığıdır. Mevcut DB sütunu dört ondalık
basamakla saklar; tam hassasiyetli yanıt `ResponsePayload` içinde korunur.

## Transaction ve idempotency

`AddJitRiskAnalysis()` Application handler ve validator kayıtlarını yapar; API bu
kaydı etkinleştirir. İstek üreten host ayrıca gerçek `ISupplierPerformanceService`
ve `IFinancialIntegrationService` adaptörlerini kaydetmelidir veya Development'ta
yukarıdaki açık simülasyon seçeneğini kullanmalıdır. UI ve SignalR bu akışın
parçası değildir.

1. `RequestJitRiskAnalysisCommandHandler`, doğrulanmış request snapshot'ını üretir.
2. Aynı transaction ve DbContext ile Pending audit ve Outbox kaydı eklenir.
3. Aynı correlation ve aynı snapshot yeniden istenirse mevcut request ID döner;
   farklı içerik correlation çakışmasıdır. Unique index eşzamanlı kayıtları korur.
4. `OutboxDispatcher`, PostgreSQL `FOR UPDATE SKIP LOCKED` ile kaydı sahiplenir.
   Başarılı broker confirm ve yönlendirme sonrası `ProcessedAtUtc` commit edilir.
5. Yanıt handler'ı audit'i correlation üzerinden `FOR UPDATE` ile kilitler ve
   supplier eşleşmesini kontrol eder. Soft-delete filtreleri audit geçmişini gizlemez.
6. `Complete()` veya `Fail()` yalnızca Pending kaydı terminal duruma geçirir.
   `ExplanationStatus` ayrıca EF concurrency token'ıdır. Request/kimlik alanları
   değiştirilemez; terminal audit güncellenemez ve audit silinemez.
7. DB commit ve scope disposal sonrasında mesaj ACK edilir.

Aynı terminal yanıt tekrar gelirse retained JSON'daki anlamsal alanlar karşılaştırılır
ve değişiklik yapmadan ACK edilir. Karşılaştırma DB'de yuvarlanan skora dayanmaz.
Aynı correlation ile farklı sonuç gelirse ilk sonuç korunur, mesaj karantinaya gider.
DB commit sonrası ACK kaybı veya publisher confirm sonrası DB commit kaybı bu
kurallarla karşılanır. Teslimat modeli at-least-once'dır.

Audit'e `ExplanationText`, `RequestMessageId`, `RequestedAtUtc`, `CompletedAtUtc`
eklenmiştir. Worker gerçek model sürümü ve inference süresi göndermediğinden model
sürümü `unreported` kalır; yerel tamamlanma zamanı inference zamanı gibi gösterilmez.
Bu akış yalnızca audit sonucunu işler; ayrıca bir RiskAssessment üretmez.

## Hatalar ve geçiş

Geçerli `IsSuccessful=false` yanıtı kalıcı Failed sonucudur ve ACK edilir.
`ErrorMessage`, `FailureMessage` alanına; yerel `ML_ANALYSIS_FAILED` kodu da
`FailureCode` alanına yazılır.

Bozuk/geçersiz yanıtlar, bilinmeyen correlation, supplier uyuşmazlığı ve çakışan
sonuçlar `supplyguard.risk.responses.error` kuyruğuna persistent/mandatory yayınlanır.
Bu yayının confirm'i alınmadan orijinal teslimat ACK edilmez. DB, bağlantı,
karantina yayını veya ACK hatasında oturum kapatılır; onaysız mesaj yeniden teslim
edilir. Her teslimat yeni DI scope/DbContext kullanır.

Eski `Features` sarmallı `risk-analysis.jit-requested.v1` Outbox kayıtları yeni
kuyruğa gönderilmez. `QuarantinedAtUtc` ve `LastError` ile tutulur; işlenmiş
işaretlenmez, silinmez. Geçerli yeni kayıtlar için yeni correlation ile yeniden
istek oluşturulmalıdır. Bilinmeyen veya bozuk retained sözleşmeler de aynı şekilde
karantinaya alınır. Eski v1 sözleşmesi ve kuyrukları ayrı kalır.

İşletimde bekleyen Outbox sayısını/yaşını, `RetryCount`, `LastError`,
`QuarantinedAtUtc`, uzun süredir Pending analizleri ve duplicate/çakışma loglarını
izleyin. İşlenmiş Outbox ve terminal audit saklama süresini tekrar teslim
penceresinden kısa tutmayın. Ham payload ve bağlantı sırları loglanmaz.

## Doğrulama

```powershell
dotnet test SupplyGuard.sln
```

Gerçek PostgreSQL/RabbitMQ testleri için sadece izole, atılabilir test veritabanı
ve broker kullanın. Testler migration uygular ve test verileri ekler:

- `SUPPLYGUARD_WORKER_TEST_POSTGRES`: geçici PostgreSQL connection string
- `RabbitMq__HostName`, `RabbitMq__Port`, `RabbitMq__UserName`,
  `RabbitMq__Password`, `RabbitMq__VirtualHost`: test broker'ı
- `SUPPLYGUARD_RUN_RABBITMQ_TESTS=1`: mevcut broker canlı testlerini de açar

```powershell
dotnet test tests/SupplyGuard.IntegrationTests --filter 'FullyQualifiedName~WorkerRiskAnalysisLiveTests|FullyQualifiedName~RabbitMqLiveTests'
```

Canlı testler gerçek satır kilidi beklemesini, SKIP LOCKED davranışını, Outbox'tan
düz request yayınını, MessageId içermeyen yanıtı, tam hassasiyetli duplicate
kontrolünü ve karantina akışını doğrular. Normal testler ayrıca commit rollback,
ACK/karantina hataları, scoped yaşam döngüsü, terminal değişmezliği ve Polly
backoff/cancellation davranışını kontrol eder.
