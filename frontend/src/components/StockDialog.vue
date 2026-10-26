<script setup lang="ts">
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import { computed, ref, watch } from 'vue'
import type { Part } from '../api/types'
import { useNotify } from '../composables/useNotify'
import { useContainersStore } from '../stores/containers'
import { usePartsStore } from '../stores/parts'

const props = defineProps<{ part: Part | null }>()
const visible = defineModel<boolean>('visible', { required: true })

const partsStore = usePartsStore()
const containers = useContainersStore()
const notify = useNotify()

const rows = ref<{ containerId: string | null; quantity: number; original: number }[]>([])
const saving = ref(false)

watch(
  () => [props.part, visible.value] as const,
  ([part, open]) => {
    if (!part || !open) return
    rows.value = part.stock.map((s) => ({ containerId: s.containerId, quantity: s.quantity, original: s.quantity }))
    if (rows.value.length === 0) rows.value.push({ containerId: null, quantity: 0, original: 0 })
    if (containers.containers.length === 0) containers.load()
  },
  { immediate: true },
)

const containerOptions = computed(() => [{ label: 'Ohne Container', value: null }, ...containers.options])

function addRow() {
  rows.value.push({ containerId: null, quantity: 1, original: 0 })
}

async function save() {
  if (!props.part) return
  const ids = rows.value.map((r) => r.containerId ?? '')
  if (new Set(ids).size !== ids.length) {
    notify.error('Jeder Container darf nur einmal vorkommen.')
    return
  }

  saving.value = true
  try {
    const removed = props.part.stock.filter((s) => !rows.value.some((r) => r.containerId === s.containerId))
    for (const s of removed) await partsStore.setStock(props.part.id, s.containerId, 0)
    for (const row of rows.value) {
      const existing = props.part.stock.find((s) => s.containerId === row.containerId)
      if (!existing || existing.quantity !== row.quantity) await partsStore.setStock(props.part.id, row.containerId, row.quantity)
    }
    notify.success('Bestand gespeichert')
    visible.value = false
  } catch (e) {
    notify.error(e)
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog v-model:visible="visible" modal :header="`Bestand: ${part?.name ?? ''}`" class="w-full max-w-lg">
    <div class="flex flex-col gap-3">
      <div v-for="(row, index) in rows" :key="index" class="flex items-center gap-2">
        <Select v-model="row.containerId" :options="containerOptions" option-label="label" option-value="value" class="flex-1" filter />
        <InputNumber v-model="row.quantity" :min="0" show-buttons button-layout="horizontal" input-class="w-16 text-center" />
        <Button icon="pi pi-trash" severity="secondary" text aria-label="Entfernen" @click="rows.splice(index, 1)" />
      </div>
      <Button label="Weiterer Lagerort" icon="pi pi-plus" text class="self-start" @click="addRow" />
    </div>
    <template #footer>
      <Button label="Abbrechen" severity="secondary" text @click="visible = false" />
      <Button label="Speichern" icon="pi pi-check" :loading="saving" @click="save" />
    </template>
  </Dialog>
</template>
