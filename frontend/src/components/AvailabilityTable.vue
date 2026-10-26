<script setup lang="ts">
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import Tag from 'primevue/tag'
import { RouterLink } from 'vue-router'
import type { PartAvailability } from '../api/types'

defineProps<{ parts: PartAvailability[] }>()
</script>

<template>
  <DataTable :value="parts" size="small">
    <Column header="Bauteil">
      <template #body="{ data }">
        <div>
          {{ data.name }}
          <span v-if="data.optional" class="text-xs text-surface-500">(optional)</span>
        </div>
        <div v-if="data.matchedPartName && data.matchedPartName !== data.name" class="text-xs text-surface-500">
          im Lager als „{{ data.matchedPartName }}“
        </div>
      </template>
    </Column>
    <Column field="quantityRequired" header="Bedarf" class="w-20 text-right" />
    <Column header="Vorhanden" class="w-44">
      <template #body="{ data }">
        <div class="flex flex-wrap items-center gap-1">
          <Tag
            :value="data.quantityMissing === 0 ? `${data.quantityInStock} da` : `${data.quantityMissing} fehlt`"
            :severity="data.quantityMissing === 0 ? 'success' : data.quantityInStock > 0 ? 'warn' : 'danger'"
          />
          <RouterLink v-for="code in data.containers" :key="code" :to="`/containers/${code}`" class="font-mono text-xs underline">
            {{ code }}
          </RouterLink>
        </div>
      </template>
    </Column>
  </DataTable>
</template>
