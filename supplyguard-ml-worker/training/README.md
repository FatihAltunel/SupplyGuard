# Model eğitimi ve sürüm yönetimi (MLflow)

Bu klasör worker imajına kopyalanmaz (Dockerfile yalnızca worker dosyalarını kopyalar).
Komutlar depo kökünden ve **`ml-worker` imajının içinde** çalıştırılır. Klasör container'a
salt okunur bağlanır; MLflow'a compose ağı üzerinden (`http://mlflow:5000`) erişilir. Böylece
`lightgbm`, `scikit-learn` ve `mlflow` sürümleri modeli yükleyecek worker ile birebir aynı olur.
v1, scikit-learn 1.7.2 ile eğitilip 1.6.1'de yüklendiği için uyumsuzluk uyarısı veriyordu.

Yerel `.venv` önerilmez: bu makinede MLflow import edilemiyor (`packaging` eksik). Kurulsa bile
sürümler imajdakinden farklı olabilir.

Kalibrasyon grafiği `matplotlib` gerektirir. İmajda bulunmadığı için atlanır; metrikler yine loglanır.
Container'da `git` olmadığından run'ın `git_commit` etiketi `unknown` olur.

Kolaylık için PowerShell'de bir kısayol tanımlayın (komutların hepsi bunu kullanır):

```powershell
function Invoke-Training { docker compose run --rm --no-deps -v "${PWD}/supplyguard-ml-worker:/work:ro" -w /work ml-worker python @args }
```

Ön koşul: `docker compose up -d mlflow` ve güncel imaj (`docker compose build ml-worker`).

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

## İlk kurulum

**Yeni ortam (ör. AWS), v1 gerekmez:** eğit ve terfi et. Henüz champion yoksa `promote.py`
challenger'ı kıyas yapmadan, kendi kapılarıyla `@champion` yapar.

```powershell
Invoke-Training training/train.py --tracking-uri http://mlflow:5000
Invoke-Training training/promote.py --tracking-uri http://mlflow:5000
docker compose up -d ml-worker
```

**Eski v1'i kıyas ve geri alma hedefi olarak almak isterseniz** (`lgbm_jit_v1.0.joblib`):
- `register_model.py` tracking adresini koda sabit (`http://localhost:5000`) yazdığı için
  container içinde çalışmaz; yalnızca MLflow'a erişebilen ve bağımlılıkları kurulu bir
  host ortamında çalıştırılabilir.
- v1 kaydedildikten sonra onu champion yapın:

```powershell
Invoke-Training training/promote.py --tracking-uri http://mlflow:5000 --bootstrap 1
```

Model ikili dosyası Git'e girmez (`.gitignore`); kaynağı MLflow artifact deposudur.
Git'te tutulan şey modeli yeniden üretecek tarif: kod, seed ve veri üreticisi parametreleri.

## Yeni sürüm

```powershell
Invoke-Training training/train.py --tracking-uri http://mlflow:5000
Invoke-Training training/promote.py --tracking-uri http://mlflow:5000 --dry-run
Invoke-Training training/promote.py --tracking-uri http://mlflow:5000
docker compose restart ml-worker
curl.exe http://localhost:8000/health   # "model_version" yeni sürümü göstermeli
```

Durum ve geri alma:

```powershell
Invoke-Training training/promote.py --tracking-uri http://mlflow:5000 --status
Invoke-Training training/promote.py --tracking-uri http://mlflow:5000 --rollback
docker compose restart ml-worker
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
