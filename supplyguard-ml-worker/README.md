# SupplyGuard ML Worker

Python 3.13 FastAPI servisi, MLflow Registry'deki
`models:/SupplyGuard_JIT_Model/latest` modelini başlangıçta yükler.
`app.state.model`, `classifier`, `explainer` ve `service` bellekte tutulur.
Model yüklenemiyorsa uygulama başlangıcı başarısız olur; mock sonuca geçilmez.

## Kurulum ve çalıştırma

Depo kökünden PowerShell:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r supplyguard-ml-worker/requirements.txt
$env:MLFLOW_TRACKING_URI = "http://localhost:5000"
$env:RABBITMQ_URL = "amqp://guest:guest@localhost/"
.\.venv\Scripts\python.exe -m uvicorn main:app --app-dir supplyguard-ml-worker --workers 1
```

`RABBITMQ_URL` ayarlanmazsa mevcut `.env` / Compose içindeki `RABBITMQ_HOST`,
`PORT`, `USER`, `PASSWORD`, `VHOST`, `USE_TLS` ayarları da desteklenir (her biri
`RABBITMQ_` önekiyle). Açıkça verilen URL önceliklidir. Bileşenlerle verilen
kimlik bilgileri URL için otomatik kodlanır.

Varsayılan MLflow adresi `http://localhost:5000`, RabbitMQ adresi
`amqp://guest:guest@localhost/` olur. Compose içinde MLflow için servis DNS adı
kullanılır. `GET /health`, model ve consumer hazırken 200; RabbitMQ bağlanırken
veya bağlantı kesildiğinde 503 döner. Bağlantı arka planda yeniden denenir.

## Mesaj sözleşmesi

İsteği RabbitMQ varsayılan exchange'ine, routing key olarak
`supplyguard.risk.requests` kullanarak JSON biçiminde yayınlayın:

```json
{
  "CorrelationId": "request-123",
  "SupplierId": "supplier-456",
  "averageDeliveryDelayDays": 2.4,
  "onTimeDeliveryRate": 0.95,
  "qualityDefectRate": 0.004,
  "DeliveryDelayTrend_Percentage": 15.2,
  "FinancialStabilityScore": 80.0,
  "ActiveRiskIndicatorsCount": 3.0
}
```

Yanıt `supplyguard.risk.responses` kuyruğuna gider. `ConfidenceScore`, risk
sınıfı `1` için 0–1 aralığında model olasılığıdır. İstatistiksel güven aralığı
anlamına gelmez. Her yanıt şu alanları içerir:

```json
{
  "CorrelationId": "request-123",
  "SupplierId": "supplier-456",
  "ConfidenceScore": 0.996,
  "ExplanationText": "Tedarikçide >%99 (Yüksek Kesinlik) oranında risk tespit edilmiştir. Riski tetikleyen ana faktörler: Ortalama Teslimat Gecikmesi (2.4 gün), Kalite Hata Oranı (%0.4). Öte yandan, Zamanında Teslimat Oranı (%95.0) durumu riski düşürme yönünde etki yapmaktadır.",
  "IsSuccessful": true,
  "ErrorMessage": null,
  "BaseValue": 1.234567,
  "Contributions": [
    {"Feature": "averageDeliveryDelayDays", "Contribution": 2.105432},
    {"Feature": "onTimeDeliveryRate", "Contribution": -0.512301}
  ]
}
```

Bu yanıt biçim örneğidir; gerçek olasılık ve faktörler modelden hesaplanır.
`Contributions` her zaman 6 özelliğin tamamını `FEATURE_NAMES` sırasıyla içerir; örnek kısaltılmıştır.
`BaseValue` ve `Contributions` yalnızca başarılı yanıtta bulunur, başarısız yanıtta `null` olur.
`BaseValue + Σ Contribution`, risk sınıfının LightGBM ham skorunu (log-odds) verir. Değerler 6 ondalığa yuvarlanır.

`onTimeDeliveryRate` ve `qualityDefectRate` için 0–1 ölçeği, trend için doğrudan
yüzde değeri kullanılır: `0.004 -> %0.4`, `15.2 -> +%15.2`. Modelin eğitiminde
kullanılan ölçeklerle aynı değerler gönderilmelidir. Pozitif SHAP katkısı en yüksek
iki, negatif katkısı en düşük bir faktör seçilir; olmayan faktörler metinde
açıkça belirtilir. SHAP katkıları LightGBM ham skor/log-odds ölçeğindedir;
yüzde puan katkısı olarak yorumlanmamalıdır.

MLflow pyfunc yalnızca genel tahmin arayüzü sunar. `get_raw_model()` ile aynı
yüklenmiş sürümün `LGBMClassifier` nesnesine erişilir; `predict_proba()` ve
`TreeExplainer` bu nesneyi kullanır. Risk etiketi `1` olmalıdır. Eğitim modelindeki
özellik adları/sırası başlangıçta doğrulanır. scikit-learn 1.6.1, mevcut modelin
eğitim sürümüne göre sabitlenmiştir.

## Hata ve teslimat davranışı

JSON, şema, tahmin veya SHAP hatasında `IsSuccessful=false`, `ConfidenceScore=0`,
`ExplanationText=""`, dolu `ErrorMessage` gönderilir. Bozuk istek mümkünse JSON'daki
kimlikleri korur; correlation bulunamazsa AMQP `correlation_id` kullanılır.
Okunamayan supplier/correlation kimlikleri boş dize olabilir.

Kalıcı kuyruklar, kalıcı yanıt mesajları, publisher confirms ve `mandatory=True`
kullanılır. İstek ancak yanıt broker tarafından onaylanınca ACK edilir.
Broker kesintisinde anında yanıt garantisi mümkün değildir; yayın başarısızsa
istek tekrar kuyruğa alınır. Broker toparlanınca yeniden işlenir. Bozuk istek,
hata yanıtı başarıyla yayınlandıktan sonra ACK edilir ve tüketimi tıkamaz.
Kapanış iptali hata yanıtına çevrilmez; onaylanmamış istek yeniden teslim edilir.

Teslimat **en az bir kez** gerçekleşir. Yayın onayından sonra istek ACK'i
kaybolursa aynı correlation için birden fazla yanıt oluşabilir. .NET consumer
`CorrelationId` üzerinden idempotent işlem yapmalıdır. İstek üreticisi de
kalıcı mesaj ve publisher confirms kullanmalıdır.

## Mevcut .NET akışıyla entegrasyon

Depodaki JIT isteği şu anda `Features` altında PascalCase alanlar kullanıyor;
eski v1 completion consumer ise farklı envelope ve kuyruklar bekliyor.
Bu worker istenen **düz JSON sözleşmesini** kullanır. .NET publisher ve consumer'ın
bu alan adları, yeni kuyruk adları ve yanıt şemasıyla eşleştirilmesi gerekir.
Yalnızca kuyruk adını değiştirmek mevcut .NET sözleşmelerini uyumlu hale getirmez.
Bu değişiklik Python worker kapsamındadır.

## Testler

```powershell
cd supplyguard-ml-worker
..\.venv\Scripts\python.exe -m unittest discover -s tests -v
```

Testler harici servis gerektirmez. Gerçek LightGBM/SHAP tutarlılığını, çıktı
biçimlerini, transport hatalarını, onay sırasını ve lifecycle davranışını kapsar.
