<script setup lang="ts">
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import { computed } from 'vue'
import { categoryLabels, partCategories, type PartFields } from '../api/types'

const model = defineModel<PartFields>({ required: true })

const categoryOptions = partCategories.map((value) => ({ value, label: categoryLabels[value] }))

const tags = computed({
  get: () => model.value.tags.join(', '),
  set: (value: string) => {
    model.value.tags = value
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean)
  },
})
</script>

<template>
  <div class="grid gap-4 sm:grid-cols-2">
    <label class="flex flex-col gap-1 sm:col-span-2">
      <span class="text-sm font-medium">Bezeichnung</span>
      <InputText v-model="model.name" required />
    </label>
    <label class="flex flex-col gap-1">
      <span class="text-sm font-medium">Kategorie</span>
      <Select v-model="model.category" :options="categoryOptions" option-label="label" option-value="value" />
    </label>
    <label class="flex flex-col gap-1">
      <span class="text-sm font-medium">Typbezeichnung</span>
      <InputText v-model="model.partNumber" placeholder="z. B. BME280" />
    </label>
    <label class="flex flex-col gap-1">
      <span class="text-sm font-medium">Hersteller</span>
      <InputText v-model="model.manufacturer" />
    </label>
    <label class="flex flex-col gap-1">
      <span class="text-sm font-medium">Bauform</span>
      <InputText v-model="model.package" placeholder="z. B. DIP-8, SOT-23, Modul" />
    </label>
    <label class="flex flex-col gap-1 sm:col-span-2">
      <span class="text-sm font-medium">Beschreibung</span>
      <Textarea v-model="model.description" rows="2" auto-resize />
    </label>
    <label class="flex flex-col gap-1">
      <span class="text-sm font-medium">Datenblatt-URL</span>
      <InputText v-model="model.datasheetUrl" type="url" />
    </label>
    <label class="flex flex-col gap-1">
      <span class="text-sm font-medium">Tags (kommagetrennt)</span>
      <InputText v-model="tags" placeholder="i2c, 3.3V" />
    </label>
  </div>
</template>
