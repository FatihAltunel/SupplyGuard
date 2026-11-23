---
description: Commit öncesi değişiklikleri, testleri, format/lint'i ve riskli noktaları kontrol et; commit atmadan onay iste
---

Commit atmadan önce yaptığın değişiklikleri kontrol et. Henüz commit/push yapma.

1. `git status` ve `git diff` (staged için `git diff --cached` da) ile tüm değişiklikleri listele. Neyi neden değiştirdiğini dosya bazında özetle. Untracked dosyaları da dahil et.

2. Testleri çalıştır ve sonucu raporla (geçen/başarısız/atlanan sayılarıyla). Başarısız olan varsa nedenini açıkla, düzeltme.
   - .NET: depo kökünden `dotnet test SupplyGuard.sln --nologo`
   - Python: `supplyguard-ml-worker` içinden `..\.venv\Scripts\python.exe -m pytest tests`; pytest kurulu değilse `..\.venv\Scripts\python.exe -m unittest discover -s tests -v`
   - Canlı testler (`RabbitMqLiveTests`, `WorkerRiskAnalysisLiveTests`) ortam değişkeni olmadan atlanır; atlandıklarını ve neyi doğrulamadığını (PostgreSQL `FOR UPDATE`/`SKIP LOCKED`, gerçek broker) belirt.

3. Format/lint çalıştır, yalnızca raporla; otomatik düzeltme uygulama.
   - .NET: `dotnet format SupplyGuard.sln --verify-no-changes`
   - Python: `..\.venv\Scripts\python.exe -m ruff check .` ve `ruff format --check .`
   - Araç kurulu değilse kurma; eksik olduğunu bildir.

4. Şunları kontrol et ve varsa bildir:
   - secret, token, şifre, connection string, `.env` içeriği (`.env`, `appsettings.*.json`, `launchSettings.json`, `docker-compose.yml`, test dosyaları; `<YOUR_...>` placeholder'ları dışındaki gerçek değerler)
   - gereksiz/debug kodu, kullanılmayan dosyalar, build çıktıları (`bin/`, `obj/`, `__pycache__/`, `*.joblib`, `mlruns/`, `.venv/`)
   - davranış değişikliği:
     - mesaj şeması: `supplyguard-ml-worker/schemas.py` ⇄ `src/SupplyGuard.Application/Common/Messaging/RiskAnalysis/Worker/V1/*.cs` (alan adları, harf büyüklüğü, tipler, zorunluluk; iki taraf birlikte değişmiş mi?)
     - kuyruk/exchange adları ve declaration argümanları (`RabbitMqTopology.cs`, `RabbitMqOptions.cs`, `appsettings.json`, `config.py`, `docker-compose.yml`)
     - DB migration: yeni/değişen dosyalar `src/SupplyGuard.Infrastructure/Migrations/` altında mı, `ModelSnapshot` güncel mi, veri kaybı veya mevcut veriyle çakışma riski var mı
     - SHAP/açıklama çıktısı: `services.py` içindeki faktör seçimi, `ExplanationText` biçimi, `ConfidenceScore` anlamı
     - HTTP endpoint sözleşmesi ve yetki policy'leri
   - eklenen/güncellenen paket bağımlılıkları (`*.csproj`, `requirements.txt`): sürüm, neden gerekli, model/eğitim sürümüyle uyum (ör. `scikit-learn`, `lightgbm`, `mlflow`)

5. İşin kapsamı dışına çıkan bir değişiklik yaptıysan açıkça belirt. `docs/ARCHITECTURE.md` etkilendiyse güncellenmesi gerekip gerekmediğini söyle.

6. Önerdiğin commit mesajını (Conventional Commits, ör. `feat(api): ...`, `fix(ml): ...`) ve branch adını yaz, onayımı bekle. `main` üzerindeysen yeni branch öner.
