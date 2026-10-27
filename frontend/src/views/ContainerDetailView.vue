<script setup lang="ts">
import Button from 'primevue/button'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import Message from 'primevue/message'
import Tag from 'primevue/tag'
import { ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { api, ApiError } from '../api/client'
import { containerTypeLabels, type ContainerDetail } from '../api/types'
import { printLabels } from '../composables/printLabels'

const props = defineProps<{ code: string }>()

const detail = ref<ContainerDetail | null>(null)
const error = ref<string | null>(null)

watch(
  () => props.code,
  async (code) => {
    detail.value = null
    error.value = null
    try {
      detail.value = await api.containers.get(code)
    } catch (e) {
      error.value = e instanceof ApiError && e.status === 404 ? `Container ${code} gibt es nicht.` : String(e)
    }
  },
  { immediate: true },
)
</script>

<template>
  <section class="flex flex-col gap-4">
    <RouterLink to="/containers" class="text-sm text-surface-500"><i class="pi pi-arrow-left" /> Alle Container</RouterLink>
    <Message v-if="error" severity="warn">{{ error }}</Message>

    <template v-if="detail">
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div class="flex items-center gap-4">
          <img :src="api.containers.labelUrl(detail.container.code)" alt="QR-Code" class="h-24 w-24 rounded bg-white p-1" />
          <div>
            <h1 class="font-mono text-3xl font-semibold">{{ detail.container.code }}</h1>
            <p class="text-surface-500">
              {{ detail.container.name ?? containerTypeLabels[detail.container.type] }}
              <template v-if="detail.container.parentCode">
                · in <RouterLink :to="`/containers/${detail.container.parentCode}`" class="underline">{{ detail.container.parentCode }}</RouterLink>
              </template>
            </p>
            <p v-if="detail.container.description" class="text-sm">{{ detail.container.description }}</p>
          </div>
        </div>
        <div class="flex gap-2">
          <Button icon="pi pi-print" severity="secondary" label="Etikett" @click="printLabels([detail.container])" />
          <RouterLink :to="{ path: '/erfassen', query: { container: detail.container.code } }">
            <Button icon="pi pi-camera" label="Bauteil hier erfassen" />
          </RouterLink>
        </div>
      </div>

      <div v-if="detail.children.length" class="flex flex-wrap gap-2">
        <span class="text-sm text-surface-500">Enthält:</span>
        <RouterLink v-for="child in detail.children" :key="child.id" :to="`/containers/${child.code}`">
          <Tag :value="child.name ? `${child.code} · ${child.name}` : child.code" />
        </RouterLink>
      </div>

      <DataTable :value="detail.contents" data-key="partId" striped-rows>
        <template #empty>Dieser Container ist leer.</template>
        <Column field="partName" header="Bauteil" sortable />
        <Column field="partNumber" header="Typ" sortable class="hidden sm:table-cell" />
        <Column field="quantity" header="Menge" sortable class="w-24 text-right" />
      </DataTable>
    </template>
  </section>
</template>
