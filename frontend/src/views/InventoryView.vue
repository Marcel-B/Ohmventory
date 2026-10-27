<script setup lang="ts">
import Button from 'primevue/button'
import Column from 'primevue/column'
import DataTable from 'primevue/datatable'
import Dialog from 'primevue/dialog'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import { useConfirm } from 'primevue/useconfirm'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { api } from '../api/client'
import { categoryLabels, type Part, type PartFields } from '../api/types'
import PartForm from '../components/PartForm.vue'
import StockDialog from '../components/StockDialog.vue'
import { useNotify } from '../composables/useNotify'
import { usePartsStore } from '../stores/parts'

const store = usePartsStore()
const notify = useNotify()
const confirm = useConfirm()

const search = ref('')
const filtered = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return store.parts
  return store.parts.filter((p) =>
    [p.name, p.partNumber, p.manufacturer, p.description, ...p.tags, ...p.stock.map((s) => s.containerCode)]
      .filter(Boolean)
      .some((v) => v!.toLowerCase().includes(q)),
  )
})

const stockPart = ref<Part | null>(null)
const stockVisible = ref(false)
function editStock(part: Part) {
  stockPart.value = part
  stockVisible.value = true
}

const editing = ref<{ id: string; fields: PartFields } | null>(null)
const saving = ref(false)
function edit(part: Part) {
  const { id, name, category, manufacturer, partNumber, package: pkg, description, datasheetUrl, tags } = part
  editing.value = { id, fields: { name, category, manufacturer, partNumber, package: pkg, description, datasheetUrl, tags: [...tags] } }
}
async function saveEdit() {
  if (!editing.value) return
  saving.value = true
  try {
    await store.update(editing.value.id, editing.value.fields)
    editing.value = null
    notify.success('Bauteil gespeichert')
  } catch (e) {
    notify.error(e)
  } finally {
    saving.value = false
  }
}

function remove(part: Part) {
  confirm.require({
    header: 'Bauteil löschen?',
    message: `„${part.name}“ und der gesamte Bestand werden gelöscht.`,
    acceptProps: { label: 'Löschen', severity: 'danger' },
    rejectProps: { label: 'Abbrechen', severity: 'secondary', text: true },
    accept: async () => {
      try {
        await store.remove(part.id)
      } catch (e) {
        notify.error(e)
      }
    },
  })
}

onMounted(() => store.load().catch(notify.error))
</script>

<template>
  <section class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold">Lager</h1>
      <div class="flex w-full gap-2 sm:w-auto">
        <IconField class="min-w-0 flex-1">
          <InputIcon class="pi pi-search" />
          <InputText v-model="search" placeholder="Suchen …" class="w-full" />
        </IconField>
        <RouterLink to="/erfassen">
          <Button icon="pi pi-camera" label="Erfassen" />
        </RouterLink>
      </div>
    </div>

    <DataTable :value="filtered" :loading="store.loading" data-key="id" striped-rows paginator :rows="25" sort-field="name" :sort-order="1">
      <template #empty>Noch keine Bauteile erfasst.</template>
      <Column header="" class="w-16">
        <template #body="{ data }">
          <img v-if="data.imageIds.length" :src="api.images.url(data.imageIds[0])" alt="" class="h-12 w-12 rounded object-cover" />
          <div v-else class="flex h-12 w-12 items-center justify-center rounded bg-surface-100 text-surface-400 dark:bg-surface-800">
            <i class="pi pi-microchip" />
          </div>
        </template>
      </Column>
      <Column field="name" header="Bauteil" sortable>
        <template #body="{ data }">
          <div class="font-medium">{{ data.name }}</div>
          <div class="text-sm text-surface-500">{{ [data.partNumber, data.manufacturer, data.package].filter(Boolean).join(' · ') }}</div>
        </template>
      </Column>
      <Column field="category" header="Kategorie" sortable class="hidden md:table-cell">
        <template #body="{ data }">
          <Tag :value="categoryLabels[data.category as Part['category']]" severity="secondary" />
        </template>
      </Column>
      <Column header="Lagerort" class="hidden sm:table-cell">
        <template #body="{ data }">
          <div class="flex flex-wrap gap-1">
            <RouterLink v-for="s in data.stock.filter((s: Part['stock'][number]) => s.containerCode)" :key="s.containerCode" :to="`/containers/${s.containerCode}`">
              <Tag :value="`${s.containerCode} (${s.quantity})`" />
            </RouterLink>
          </div>
        </template>
      </Column>
      <Column field="totalQuantity" header="Menge" sortable class="w-24 text-right" />
      <Column>
        <template #body="{ data }">
          <div class="flex flex-col items-end sm:flex-row sm:justify-end">
            <Button icon="pi pi-box" text rounded aria-label="Bestand" v-tooltip.top="'Bestand'" @click="editStock(data)" />
            <Button icon="pi pi-pencil" text rounded severity="secondary" aria-label="Bearbeiten" @click="edit(data)" />
            <Button icon="pi pi-trash" text rounded severity="danger" aria-label="Löschen" @click="remove(data)" />
          </div>
        </template>
      </Column>
    </DataTable>

    <StockDialog v-model:visible="stockVisible" :part="stockPart" />

    <Dialog :visible="editing !== null" modal header="Bauteil bearbeiten" class="w-full max-w-2xl" @update:visible="editing = null">
      <PartForm v-if="editing" v-model="editing.fields" />
      <template #footer>
        <Button label="Abbrechen" severity="secondary" text @click="editing = null" />
        <Button label="Speichern" icon="pi pi-check" :loading="saving" @click="saveEdit" />
      </template>
    </Dialog>
  </section>
</template>
