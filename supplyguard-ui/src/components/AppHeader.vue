<script setup lang="ts">
defineProps<{ signedIn: boolean }>()
defineEmits<{ signOut: [] }>()
</script>

<template>
  <header class="app-header">
    <div class="shell header-inner">
      <a class="brand" href="/" aria-label="SupplyGuard ana sayfa">
        <svg class="brand-mark" viewBox="0 0 32 32" aria-hidden="true">
          <path d="M16 2 4 7v8c0 7.4 5.1 13.4 12 15 6.9-1.6 12-7.6 12-15V7L16 2Z" fill="currentColor" />
          <path d="m10.5 16.2 3.8 3.8 7.2-7.6" fill="none" stroke="#0b1f3a" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
        <span class="brand-text">
          <strong>SupplyGuard</strong>
          <small lang="en">Supplier Risk Intelligence</small>
        </span>
      </a>
      <nav v-if="!signedIn" class="header-nav" aria-label="Sayfa bölümleri">
        <a href="#platform">Platform</a>
        <a href="#nasil-calisir">Nasıl çalışır</a>
        <a href="#model">Model</a>
      </nav>
      <div v-else class="header-session">
        <span class="badge">Demo oturumu</span>
        <button class="btn btn-ghost-light btn-small" type="button" @click="$emit('signOut')">Çıkış</button>
      </div>
    </div>
  </header>
</template>
