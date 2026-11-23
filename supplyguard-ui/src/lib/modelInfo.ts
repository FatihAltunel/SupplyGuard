// Static description of the model behind the demo. Source of truth:
// supplyguard-ml-worker/services.py (MODEL_URI, TreeExplainer) and schemas.py (FEATURE_NAMES).
// The worker does not report the resolved registry version, so it is shown as "latest".
export const MODEL_INFO = {
  name: 'SupplyGuard_JIT_Model',
  registry: 'MLflow Model Registry',
  version: 'latest (sürüm numarası henüz raporlanmıyor)',
  algorithm: 'LightGBM ikili sınıflandırıcı',
  explainer: 'SHAP TreeExplainer',
  features: 6,
  output: 'Risk sınıfının olasılığı (%0–100)'
} as const
