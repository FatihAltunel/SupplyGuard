// Static description of the model behind the demo. Source of truth:
// supplyguard-ml-worker/services.py (DEFAULT_MODEL_URI, TreeExplainer) and schemas.py (FEATURE_NAMES).
// The worker loads the @champion alias; the API does not expose the resolved version number,
// which is visible in the worker's GET /health ("model_version").
export const MODEL_INFO = {
  name: 'SupplyGuard_JIT_Model',
  registry: 'MLflow Model Registry',
  version: '@champion (kalite testlerini geçip yayına alınan sürüm)',
  algorithm: 'LightGBM ikili sınıflandırıcı',
  // No tree count here: it changes with every training run (early stopping), so a constant goes stale.
  explainer: 'SHAP TreeExplainer',
  features: 6,
  output: 'Risk sınıfının olasılığı (%0–100)'
} as const
