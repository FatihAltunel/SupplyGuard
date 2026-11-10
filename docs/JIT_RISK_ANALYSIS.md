# JIT risk analizi: Pure DI, CQRS ve Outbox

Bu örnek istenen JSON gövdesini `risk-analysis.jit-requested.v1` mesaj tipiyle üretir.
Mevcut `risk-analysis.requested.v1`, `/api/suppliers/{id}/analyze` ve ona bağlı XAI
completion akışı ayrı kalır. Yeni handler isteği hazırlayıp Outbox'a kaydeder;
FastAPI'ye HTTP çağrısı yapmaz. Veritabanı şema değişikliği gerektirmez.

## Katmanlar ve veri kaynakları

| Değer | Kaynak |
| --- | --- |
| `AverageDeliveryDelayDays` | Komuttaki toplulaştırılmış performans metriği |
| `OnTimeDeliveryRate` | Komuttaki toplulaştırılmış performans metriği |
| `QualityDefectRate` | Komuttaki toplulaştırılmış kalite metriği |
| `DeliveryDelayTrend_Percentage` | `ISupplierPerformanceService` |
| `FinancialStabilityScore` | `IFinancialIntegrationService` |
| `ActiveRiskIndicatorsCount` | EF Core ile `risk_indicators` sayımı |

İlk üç metriği hesaplayacak satın alma/kalite kayıtları mevcut olmadığından bunlar
komuta girilir. API sınırında bu değerlerin güvenilir bir toplulaştırılmış veri
kaynağından gelmesi gerekir. Risk göstergelerinin `RawValue` alanları, satın alma
siparişlerinin yerine kullanılmaz.

Application katmanı yalnızca arayüzlere bağımlıdır. EF Core sorgusu Infrastructure
katmanındaki `SupplierRiskIndicatorReader` içinde bulunur:

```csharp
public Task<int> CountActiveAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
    dbContext.RiskIndicators.AsNoTracking()
        .CountAsync(indicator => indicator.SupplierId == supplierId && indicator.IsActive,
            cancellationToken);
```

Bu sorgu SQL `COUNT` üretir; kayıtları belleğe almaz. Mevcut global query filter
nedeniyle soft-delete edilmiş göstergeler sayılmaz. `ExpiresAtUtc` için ek bir
filtre uygulanmaz: aktiflik tanımı bu kontratta `IsActive == true` koşuludur.

Servis sözleşmeleri:

```csharp
public interface ISupplierPerformanceService
{
    Task<decimal?> GetDeliveryDelayTrendAsync(
        Guid supplierId, TimeSpan timespan, CancellationToken cancellationToken = default);
}

public interface IFinancialIntegrationService
{
    Task<decimal> GetStabilityScoreAsync(
        Guid supplierId, CancellationToken cancellationToken = default);
}
```

## Metriklerin anlamı

- Gecikme gün cinsinden ve negatif olmayan bir ortalamadır; erken teslimat bu
  tanımda negatif gecikme oluşturmaz.
- Teslimat ve kusur oranları `0..1` aralığındadır: `0.95`, %95 anlamına gelir.
- Trend yüzde değişimidir: `(güncel ortalama - önceki ortalama) / önceki ortalama * 100`.
  Pozitif değer gecikmenin arttığını, negatif değer iyileşmeyi gösterir.
- Finansal istikrar skoru `0..100` aralığındadır; yüksek skor daha istikrarlı demektir.

Handler `TimeSpan.FromDays(30)` kullanır: `[t-30 gün, t)` ile `[t-60 gün, t-30 gün)`
karşılaştırılır. Bu, takvim ayı değil, ardışık iki 30 günlük penceredir. Takvim ayı
isteniyorsa gerçek adaptörün sözleşmesi açık tarih aralıklarıyla değiştirilmelidir.
Gerçek performans adaptörü iki pencereyi tek bir sabit `t` üzerinden okumalıdır.

Simülasyonda önceki ortalama `1.00`, güncel ortalama `1.15` gündür: sonuç `%15`.
Bu gözlemler sentetiktir ve tüm tedarikçiler için aynı demo sonucunu üretir.
Finans servisi de açıkça demo amaçlı `85` döner. Gerçek entegrasyonda temel
performans metrikleri ve trend aynı gözlem penceresine dayanmalıdır.

Bir pencere boşsa veya önceki ortalama sıfırsa göreli değişim hesaplanamaz;
performans servisi `null`, handler `RiskAnalysis.TrendUnavailable` döner ve Outbox'a
kayıt eklemez. Finansal veri yoksa gerçek adaptör açık hata vermelidir. Dış servis
hataları ve iptaller aktarılır; eksik veri sağlıklı bir skorla doldurulmaz.

Bu metrik tanımları proje/model sözleşmesidir; tek başına bir otomotiv standardına
uygunluk sertifikası veya JIT süreç uygunluğu iddiası oluşturmaz.

## DI kaydı ve çağrı örneği

Mevcut composition root'ta `AddApplication`, `AddInfrastructure` ve
`AddRabbitMqMessaging` zaten çağrılıyor. JIT örneğini kullanacak bir development
host'una aşağıdaki iki kaydı **birlikte** ekleyin:

```csharp
using SupplyGuard.Application;
using SupplyGuard.Infrastructure;

builder.Services.AddJitRiskAnalysis();
builder.Services.AddSimulatedSupplierIntegrations(); // Yalnızca demo/development.
```

Üretimde ikinci satır yerine gerçek `ISupplierPerformanceService` ve
`IFinancialIntegrationService` adaptörlerini scoped olarak kaydedin. Simülasyonlar
ve JIT handler varsayılan API başlangıcında otomatik etkinleştirilmez.

Controller veya diğer bir uygulama giriş noktası handler'ı constructor üzerinden alır:

```csharp
public sealed class JitAnalysisUseCase(
    ICommandHandler<RequestJitRiskAnalysisCommand, RequestJitRiskAnalysisResult> handler)
{
    public Task<Result<RequestJitRiskAnalysisResult>> ExecuteAsync(
        Guid supplierId, CancellationToken cancellationToken) =>
        handler.HandleAsync(new RequestJitRiskAnalysisCommand(
            supplierId, "req-998877", 1.5m, 0.95m, 0.015m), cancellationToken);
}
```

Bu kullanım için `SupplyGuard.Application.Common.CQRS` ve
`SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis` namespace'lerini
import edin. `req-998877` yalnızca örnektir; her yeni analiz için benzersiz bir
correlation ID kullanın. Komut validasyonu kayıtlı CQRS decorator'ı üzerinden çalışır.
Başarılı sonuç Outbox'a dayanıklı kabulü gösterir; API bunu `202 Accepted` olarak
dönebilir. Bu değişiklik yeni bir HTTP endpoint eklemez.

## Transaction ve JSON

`RequestJitRiskAnalysisCommandHandler` tedarikçiyi doğrular, dış servislerden
özellikleri alır, aktif risk sayısını sorgular ve tüm özellikleri doğrular. Dış
servis çağrıları transaction başlamadan önce yapılır. EF sorguları aynı scoped
DbContext üzerinde sıralı çalıştırılır.

Ardından mevcut `IOutboxWriter` ve `IUnitOfWork` kullanılır:

```csharp
await unitOfWork.BeginTransactionAsync(cancellationToken);
try
{
    await outboxWriter.WriteAsync(message, cancellationToken);
    await unitOfWork.CommitAsync(cancellationToken);
}
catch
{
    await unitOfWork.RollbackAsync(CancellationToken.None);
    throw;
}
```

`OutboxWriter` serializer ile gövdeyi üretir ve aşağıdaki entity'yi ortak DbContext'e
ekler; `CommitAsync`, `SaveChangesAsync` ve transaction commit işlemlerini yapar:

```csharp
var outboxMessage = new OutboxMessage(
    message.MessageId, message.MessageType, serializer.Serialize(message),
    message.CorrelationId, message.OccurredAtUtc);
```

Kaydedilen ve yayımlanan JSON gövdesi tam olarak şu alanları içerir. JSON sayıların
sonundaki sıfırların yazılışını garanti etmez; `15` ve `15.0` aynı sayısal değerdir.

```json
{
  "SupplierId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "CorrelationId": "req-998877",
  "Features": {
    "AverageDeliveryDelayDays": 1.5,
    "OnTimeDeliveryRate": 0.95,
    "QualityDefectRate": 0.015,
    "DeliveryDelayTrend_Percentage": 15.0,
    "FinancialStabilityScore": 85,
    "ActiveRiskIndicatorsCount": 3
  }
}
```

`JsonPropertyName` alan adlarının harf büyüklüğünü ve alt çizgiyi sabitler. Serializer
eksik, bilinmeyen veya tekrarlanan alanları, string biçimindeki sayıları ve aralık
dışı değerleri reddeder. `MessageId` ve `OccurredAtUtc` gövdeye eklenmez; Outbox
kolonlarında saklanır ve AMQP `MessageId`/`Timestamp` property'lerine aktarılır.
`CorrelationId` hem gövdede hem AMQP property olarak yer alır ve bir string'dir.

Background dispatcher commit edilmiş kaydı okuyup RabbitMQ publisher'a verir.
Tekrar denemede özellikler yeniden hesaplanmaz; Outbox'taki gövde ve metadata
kullanılır. Bu kayıt XAI için modelin hangi girdiyi gördüğünün kalıcı kaynağıdır.
Farklı kaynaklardan gelen özelliklerin okunması dağıtık, atomik bir snapshot değildir;
üretim adaptörleri için gözlem penceresi ve veri güncelliği ayrıca tanımlanmalıdır.

## Worker ve XAI geçiş sınırı

Bu değişiklik request/Outbox tarafını uygular. Repository'deki mevcut Python worker
eski camelCase v1 envelope'unu doğrular ve yeni JIT gövdesini henüz tüketmez.
Yeni worker sözleşmesini devreye alırken ayrı, durable bir JIT kuyruğunu
`RabbitMq:ExchangeName` (varsayılan `supplyguard.events`) exchange'ine
`risk-analysis.jit-requested.v1` routing key'iyle bağlayın. Mevcut publisher bu tipi
mesajın `MessageType` değeriyle yayımlar; JIT kuyruğunu otomatik oluşturmaz.
Binding yoksa mandatory publish başarısız olur ve Outbox kaydı retry için korunur.

JIT worker `MessageId` ve timestamp'i AMQP property'lerinden, correlation ID'yi
gövdeden/property'den okumalıdır. At-least-once teslimatta aynı `MessageId` ile
yeniden gelen iş idempotent ele alınmalıdır. Yeni gövdeyi eski strict v1 kuyruğuna
bağlamak yerine worker modeli, routing ve completion sözleşmesini birlikte
güncelleyin. Mevcut completion handler GUID correlation ve eski request snapshot'ını
beklediğinden JIT sonuçlarını doğrudan kabul etmez.

JIT completion/XAI adaptöründe response'u `RequestMessageId`, `SupplierId` ve string
`CorrelationId` üzerinden bu Outbox kaydıyla eşleştirin; audit'e saklanan request
gövdesini aktarın. Açıklamadaki feature adlarını burada tanımlanan altı adla aynı
tutun. Mevcut append-only audit ve idempotency kuralları korunmalıdır. Bu örnek
worker veya completion sözleşmesini değiştirmez.

## Doğrulama

```powershell
dotnet test SupplyGuard.sln --no-restore
```

`JitRiskAnalysisTests` gerçek EF Core/SQLite sorgusuyla tedarikçi, aktiflik ve
soft-delete filtrelerini; DI çözümlemesini; JSON gövdesini; trend yönünü; eksik veri,
servis hatası ve iptal davranışını; rollback'i ve retry sırasında snapshot/kimliklerin
korunmasını doğrular. Canlı PostgreSQL ve RabbitMQ doğrulaması bu testin kapsamında değildir.
