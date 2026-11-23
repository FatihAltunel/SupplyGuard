# Model eğitimi ve sürüm yönetimi (MLflow)

Bu klasör worker imajına kopyalanmaz (Dockerfile yalnızca worker dosyalarını kopyalar).
Komutlar depo kökünden, **worker ile aynı sanal ortamda** çalıştırılır; `lightgbm` ve
`scikit-learn` sürümleri `requirements.txt` içindeki sabit sürümlerle aynı olmalıdır.
Kalibrasyon grafiği için isteğe bağlı olarak `matplotlib` gerekir; yoksa atlanır.

## Yaşam döngüsü

```
train.py  ──►  Registry'de yeni sürüm  ──(kapılar geçtiyse)──►  @challenger
promote.py ──►  champion ile aynı referans veride kıyas  ──(geçerse)──►  @champion
docker compose restart ml-worker  ──►  worker @champion'u yükler, /health sürümü gösterir
promote.py --rollback  ──►  @champion önceki sürüme döner
```

| Alias | Anlamı |
|---|---|
| `@champion` | Canlıdaki sürüm. Worker varsayılan olarak bunu yükler. |
| `@challenger` | Kendi kapılarını geçmiş, terfi bekleyen aday. |
| `@previous_champion` | Geri alma hedefi. |

## İlk kurulum (mevcut `lgbm_jit_v1.0.joblib`)

```powershell
docker compose up -d mlflow
.\.venv\Scripts\python.exe supplyguard-ml-worker/register_model.py          # dosya supplyguard-ml-worker/ altında olmalı
.\.venv\Scripts\python.exe supplyguard-ml-worker/training/promote.py --bootstrap 1
```

Model ikili dosyası Git'e girmez (`.gitignore`); kaynağı MLflow artifact deposudur.
Git'te tutulan şey modeli yeniden üretecek tarif: kod, seed ve veri üreticisi parametreleri.

## Yeni sürüm

```powershell
.\.venv\Scripts\python.exe supplyguard-ml-worker/training/train.py
.\.venv\Scripts\python.exe supplyguard-ml-worker/training/promote.py --dry-run
.\.venv\Scripts\python.exe supplyguard-ml-worker/training/promote.py
docker compose restart ml-worker
curl http://localhost:8000/health      # "model_version" yeni sürümü göstermeli
```

Challenger'ı terfi etmeden uçtan uca denemek için `.env` içine
`MLFLOW_MODEL_URI=models:/SupplyGuard_JIT_Model@challenger` yazıp worker'ı yeniden başlatın,
UI'dan senaryoları deneyin, sonra satırı silip tekrar başlatın.

## Kalite kapıları (`model_checks.py`)

1. **Sözleşme:** sınıflar `[0, 1]`, özellik adı/sırası `schemas.FEATURE_NAMES` ile aynı.
2. **İstatistik:** AUC ≥ 0.80, ECE ≤ 0.05, uç tahmin oranı ≤ %5, ortalama tahmin ile gerçek oran farkı ≤ 0.03.
3. **Davranış:** monotonluk ihlali sıfır; altı golden case beklenen aralıkta ve risk sırası doğru.
4. **Kıyas:** aynı referans veride AUC champion'dan 0.01'den fazla düşmez, Brier 0.005'ten fazla artmaz.
5. **SHAP:** `BaseValue + ΣSHAP` ham log-odds'u veriyor (worker'daki açıklama yolu bu modelde çalışıyor).

Eşikleri değiştirmek bir iş kararıdır; her run'da `gate_*` parametresi olarak loglanır.

## Bilinen sınırlar

- Veri sentetiktir. Model, `data_generator.py` içindeki `GeneratorConfig` ağırlıklarını
  (iş varsayımlarını) öğrenir; metrikler gerçek dünya performansı değildir.
- Ağaç modelleri eğitim aralığının dışında düzleşir (ör. %99'luk dilimin üstündeki gecikmeler).
- Worker modeli yalnızca başlangıçta yükler; alias değişikliği restart gerektirir.
