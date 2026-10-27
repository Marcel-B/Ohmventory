<script setup lang="ts">
import ConfirmDialog from 'primevue/confirmdialog'
import Toast from 'primevue/toast'
import { RouterLink, RouterView } from 'vue-router'

const navigation = [
  { to: '/lager', label: 'Lager', icon: 'pi pi-box' },
  { to: '/erfassen', label: 'Erfassen', icon: 'pi pi-camera' },
  { to: '/containers', label: 'Container', icon: 'pi pi-th-large' },
  { to: '/assistent', label: 'Assistent', icon: 'pi pi-sparkles' },
  { to: '/projekte', label: 'Projekte', icon: 'pi pi-folder' },
  { to: '/einkaufsliste', label: 'Einkauf', icon: 'pi pi-shopping-cart' },
]
</script>

<template>
  <Toast position="top-center" />
  <ConfirmDialog />
  <div class="flex min-h-screen flex-col">
    <header class="sticky top-0 z-10 border-b border-surface-200 bg-surface-0/90 backdrop-blur dark:border-surface-800 dark:bg-surface-900/90">
      <div class="mx-auto flex max-w-6xl items-center gap-6 px-4 py-3">
        <RouterLink to="/" class="flex items-center gap-2 text-lg font-semibold">
          <span class="flex h-8 w-8 items-center justify-center rounded-lg bg-primary text-primary-contrast">Ω</span>
          Ohmventory
        </RouterLink>
        <nav class="hidden gap-1 md:flex">
          <RouterLink
            v-for="item in navigation"
            :key="item.to"
            :to="item.to"
            class="flex items-center gap-2 rounded-lg px-3 py-2 text-sm text-surface-600 hover:bg-surface-100 dark:text-surface-300 dark:hover:bg-surface-800"
            active-class="!bg-primary-50 !text-primary-700 dark:!bg-primary-950 dark:!text-primary-300"
          >
            <i :class="item.icon" />
            {{ item.label }}
          </RouterLink>
        </nav>
      </div>
    </header>

    <main class="mx-auto w-full max-w-6xl flex-1 px-4 pt-6 pb-24 md:pb-8">
      <RouterView />
    </main>

    <nav class="fixed inset-x-0 bottom-0 z-10 grid grid-cols-6 border-t border-surface-200 bg-surface-0 md:hidden dark:border-surface-800 dark:bg-surface-900">
      <RouterLink
        v-for="item in navigation"
        :key="item.to"
        :to="item.to"
        class="flex flex-col items-center gap-1 py-2 text-[11px] text-surface-500"
        active-class="!text-primary-600 dark:!text-primary-400"
      >
        <i :class="item.icon" class="text-lg" />
        {{ item.label }}
      </RouterLink>
    </nav>
  </div>
</template>
