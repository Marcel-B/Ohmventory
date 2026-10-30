<script setup lang="ts">
import Button from 'primevue/button'
import Card from 'primevue/card'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import Message from 'primevue/message'
import ProgressBar from 'primevue/progressbar'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api } from '../api/client'
import type { PartFields, RecognitionResponse } from '../api/types'
import LlmPausedNotice from '../components/LlmPausedNotice.vue'
import PartForm from '../components/PartForm.vue'
import { isHeic, resizeImage, rotateImage } from '../composables/resizeImage'
import { useLlmStatus } from '../composables/useLlmStatus'
import { useNotify } from '../composables/useNotify'
import { useContainersStore } from '../stores/containers'
import { usePartsStore } from '../stores/parts'

const route = useRoute()
const router = useRouter()
const notify = useNotify()
const containers = useContainersStore()
const parts = usePartsStore()
const { status: llmStatus, refresh: refreshLlmStatus } = useLlmStatus()

const fileInput = ref<HTMLInputElement>()
const image = ref<Blob | null>(null)
const preview = ref<string | null>(null)
const hint = ref('')
const recognizing = ref(false)
const recognitionError = ref<string | null>(null)
const result = ref<RecognitionResponse | null>(null)
const manual = ref(false)

const emptyFields = (): PartFields => ({
  name: '',
  category: 'Other',
  manufacturer: null,
  partNumber: null,
  package: null,
  description: null,
  datasheetUrl: null,
  tags: [],
})
const fields = ref<PartFields>(emptyFields())
const quantity = ref(1)
const containerId = ref<string | null>(null)
const saving = ref(false)

const containerOptions = computed(() => [{ label: 'Ohne Container', value: null }, ...containers.options])
const showForm = computed(() => manual.value || result.value !== null)

async function onFile(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  reset()
  try {
    image.value = await resizeImage(file).catch(async (e) => {
      if (!isHeic(file)) throw e
      return resizeImage(new File([await api.images.convert(file)], 'foto.jpg', { type: 'image/jpeg' }))
    })
    preview.value = URL.createObjectURL(image.value)
  } catch (e) {
    notify.error(e)
  }
}

function reset() {
  if (preview.value) URL.revokeObjectURL(preview.value)
  image.value = null
  preview.value = null
  result.value = null
  recognitionError.value = null
  manual.value = false
  fields.value = emptyFields()
  quantity.value = 1
}

async function recognize() {
  if (!image.value) return
  recognizing.value = true
  recognitionError.value = null
  try {
    // Gedrehte Ansichten nutzt das Backend nur, wenn es im Original keinen brauchbaren Aufdruck findet.
    const views = await Promise.all([rotateImage(image.value, true), rotateImage(image.value, false)]).catch(() => [])
    const response = await api.recognition.recognize(image.value, hint.value || undefined, views)
    result.value = response
    const r = response.recognized
    fields.value = {
      name: r.name,
      category: r.isBreakoutBoard && r.category === 'Other' ? 'BreakoutBoard' : r.category,
      manufacturer: r.manufacturer,
      partNumber: r.partNumber,
      package: r.package,
      description: r.description,
      datasheetUrl: null,
      tags: [],
    }
  } catch (e) {
    recognitionError.value = e instanceof Error ? e.message : String(e)
    refreshLlmStatus()
  } finally {
    recognizing.value = false
  }
}

async function addToExisting(partId: string) {
  saving.value = true
  try {
    const part = await api.parts.get(partId)
    const current = part.stock.find((s) => s.containerId === containerId.value)?.quantity ?? 0
    await parts.setStock(partId, containerId.value, current + quantity.value)
    if (result.value) await api.images.assign(result.value.imageId, partId)
    notify.success('Bestand erhöht', `${part.name}: +${quantity.value}`)
    reset()
  } catch (e) {
    notify.error(e)
  } finally {
    saving.value = false
  }
}

async function createPart() {
  if (!fields.value.name.trim()) {
    notify.error('Bitte eine Bezeichnung angeben.')
    return
  }
  saving.value = true
  try {
    const part = await parts.create(fields.value, quantity.value, containerId.value, result.value?.imageId ?? null)
    notify.success('Bauteil angelegt', part.name)
    reset()
  } catch (e) {
    notify.error(e)
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  await containers.load().catch(notify.error)
  const code = route.query.container
  if (typeof code === 'string') containerId.value = containers.containers.find((c) => c.code === code.toUpperCase())?.id ?? null
})
</script>

<template>
  <section class="mx-auto flex max-w-3xl flex-col gap-4">
    <h1 class="text-2xl font-semibold">Bauteil erfassen</h1>
    <LlmPausedNotice :status="llmStatus" />

    <Card>
      <template #content>
        <div class="flex flex-col gap-4">
          <input ref="fileInput" type="file" accept="image/*,.heic,.heif" capture="environment" class="hidden" @change="onFile" />
          <div
            class="flex min-h-56 cursor-pointer items-center justify-center overflow-hidden rounded-xl border-2 border-dashed border-surface-300 dark:border-surface-700"
            @click="fileInput?.click()"
          >
            <img v-if="preview" :src="preview" alt="Vorschau" class="max-h-96 object-contain" />
            <div v-else class="flex flex-col items-center gap-2 p-6 text-center text-surface-500">
              <i class="pi pi-camera text-4xl" />
              <span>Foto aufnehmen oder auswählen</span>
              <span class="text-sm">Breakout-Board oder Nahaufnahme mit lesbarer Beschriftung</span>
            </div>
          </div>
          <div class="flex flex-wrap gap-2">
            <InputText v-model="hint" placeholder="Optionaler Hinweis, z. B. „Sensor aus dem 37er-Kit“" class="min-w-0 flex-1" />
            <Button label="Erkennen" icon="pi pi-sparkles" :disabled="!image || llmStatus?.paused" :loading="recognizing" @click="recognize" />
          </div>
          <ProgressBar v-if="recognizing" mode="indeterminate" class="h-1" />
          <Message v-if="recognitionError" severity="error">{{ recognitionError }}</Message>
          <Button v-if="!showForm" label="Ohne Foto manuell erfassen" link class="self-start p-0" @click="manual = true" />
        </div>
      </template>
    </Card>

    <Card v-if="result">
      <template #title>Erkannt: {{ result.recognized.name }}</template>
      <template #content>
        <div class="flex flex-col gap-3">
          <div class="flex flex-wrap items-center gap-2 text-sm">
            <Tag
              :value="`Sicherheit ${Math.round(result.recognized.confidence * 100)} %`"
              :severity="result.recognized.confidence >= 0.7 ? 'success' : result.recognized.confidence >= 0.4 ? 'warn' : 'danger'"
            />
            <Tag v-if="result.recognized.isBreakoutBoard" value="Breakout-Board" severity="info" />
            <span v-if="result.recognized.markingsRead.length" class="text-surface-500">
              Gelesen: {{ result.recognized.markingsRead.join(', ') }}
            </span>
          </div>
          <p v-if="result.recognized.observations" class="text-sm text-surface-500">
            <i class="pi pi-eye mr-1" />{{ result.recognized.observations }}
          </p>
          <p v-if="result.recognized.alternatives.length" class="text-sm text-surface-500">
            Alternativen: {{ result.recognized.alternatives.join(', ') }}
          </p>
          <div v-if="result.recognized.sources.length" class="flex flex-col gap-1 rounded-lg bg-surface-100 p-3 text-sm dark:bg-surface-800">
            <span class="font-medium"><i class="pi pi-globe mr-1" />Per Websuche bestimmt</span>
            <span v-if="result.recognized.webFinding" class="text-surface-600 dark:text-surface-300">{{ result.recognized.webFinding }}</span>
            <a
              v-for="source in result.recognized.sources"
              :key="source.url"
              :href="source.url"
              target="_blank"
              rel="noopener noreferrer"
              class="truncate text-primary hover:underline"
            >{{ source.title }}</a>
          </div>
          <Message v-if="result.recognized.confidence < 0.6" severity="secondary" size="small">
            Unsicher. Hilfreich sind ein Foto der Seite mit der Beschriftung oder ein Hinweis wie „Display“, dann noch einmal erkennen.
          </Message>
          <div v-if="result.matches.length" class="flex flex-col gap-2">
            <span class="font-medium">Schon im Lager:</span>
            <div
              v-for="match in result.matches"
              :key="match.partId"
              class="flex items-center justify-between gap-2 rounded-lg bg-surface-100 p-3 dark:bg-surface-800"
            >
              <span>{{ match.name }} <span class="text-surface-500">({{ match.totalQuantity }} Stk.)</span></span>
              <Button :label="`+${quantity} hinzufügen`" size="small" :loading="saving" @click="addToExisting(match.partId)" />
            </div>
          </div>
        </div>
      </template>
    </Card>

    <Card v-if="showForm">
      <template #title>{{ result?.matches.length ? 'Oder als neues Bauteil anlegen' : 'Bauteil anlegen' }}</template>
      <template #content>
        <div class="flex flex-col gap-4">
          <PartForm v-model="fields" />
          <div class="grid gap-4 sm:grid-cols-2">
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">Menge</span>
              <InputNumber v-model="quantity" :min="0" show-buttons button-layout="horizontal" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="text-sm font-medium">Container</span>
              <Select v-model="containerId" :options="containerOptions" option-label="label" option-value="value" filter />
            </label>
          </div>
          <div class="flex justify-end gap-2">
            <Button label="Verwerfen" severity="secondary" text @click="reset" />
            <Button label="Speichern" icon="pi pi-check" :loading="saving" @click="createPart" />
          </div>
        </div>
      </template>
    </Card>

    <Button v-if="!showForm" label="Zum Lager" icon="pi pi-arrow-left" text class="self-start" @click="router.push('/lager')" />
  </section>
</template>
