<script setup lang="ts">
import { formatEffect, type ContributionRow } from '../lib/explanation'

// Diverging bars around a centre axis: left = lowered the risk, right = raised it.
defineProps<{ rows: ContributionRow[] }>()
</script>

<template>
  <div class="contrib">
    <h3>Neden bu sonuç?</h3>
    <p class="muted small">
      Her satır, girdiğiniz bir değerin modelin kararını ne yöne ve ne kadar ittiğini gösterir (SHAP).
      Çubuk uzadıkça etki büyür.
    </p>
    <div class="contrib-legend" aria-hidden="true">
      <span class="legend-down">◀ Riski azalttı</span>
      <span class="legend-up">Riski artırdı ▶</span>
    </div>
    <ul>
      <li v-for="row in rows" :key="row.key" :class="row.direction">
        <div class="contrib-label">
          <span>{{ row.label }} <small v-if="row.value" class="muted">· {{ row.value }}</small></span>
          <span class="contrib-strength">
            {{ row.direction === 'up' ? 'Artırdı' : row.direction === 'down' ? 'Azalttı' : 'Etkisiz' }}
            <template v-if="row.direction !== 'none'"> · {{ row.strength }}</template>
            <code :title="`Log-odds katkısı: ${formatEffect(row.contribution)}`">{{ formatEffect(row.contribution) }}</code>
          </span>
        </div>
        <div class="contrib-track" role="img"
             :aria-label="`${row.label}: ${row.direction === 'up' ? 'riski artırdı' : row.direction === 'down' ? 'riski azalttı' : 'etkisiz'}, ${row.strength}`">
          <div class="contrib-half left">
            <div v-if="row.direction === 'down'" class="contrib-bar down" :style="{ width: `${row.width}%` }" />
          </div>
          <div class="contrib-half right">
            <div v-if="row.direction === 'up'" class="contrib-bar up" :style="{ width: `${row.width}%` }" />
          </div>
        </div>
      </li>
    </ul>
  </div>
</template>
