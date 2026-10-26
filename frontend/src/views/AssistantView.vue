<script setup lang="ts">
import Button from 'primevue/button'
import Card from 'primevue/card'
import Message from 'primevue/message'
import Panel from 'primevue/panel'
import ProgressBar from 'primevue/progressbar'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { ref } from 'vue'
import { api } from '../api/client'
import type { ProjectProposal } from '../api/types'
import AvailabilityTable from '../components/AvailabilityTable.vue'
import LlmPausedNotice from '../components/LlmPausedNotice.vue'
import { useLlmStatus } from '../composables/useLlmStatus'
import { useNotify } from '../composables/useNotify'
import { useAssistantStore } from '../stores/assistant'

const store = useAssistantStore()
const notify = useNotify()
const { status: llmStatus, refresh: refreshLlmStatus } = useLlmStatus()

const examples = ['Was kann ich mit meinem Lager bauen?', 'Kann ich ein Thermometer bauen?', 'Ein kleines Projekt mit einem Display für das Wochenende']
const saved = ref(new Map<string, string>())
const busy = ref<string | null>(null)

const missingCount = (p: ProjectProposal) => p.parts.filter((x) => x.quantityMissing > 0 && !x.optional).length

async function save(project: ProjectProposal) {
  const existing = saved.value.get(project.name)
  if (existing) return existing
  const created = await api.projects.save({
    name: project.name,
    description: project.description,
    question: store.answer?.question ?? null,
    sources: project.sources,
    parts: project.parts
      .filter((p) => !p.optional)
      .map((p) => ({ name: p.name, partNumber: p.partNumber, quantity: p.quantityRequired, optional: false })),
  })
  saved.value.set(project.name, created.id)
  return created.id
}

async function saveProject(project: ProjectProposal) {
  busy.value = project.name
  try {
    await save(project)
    notify.success('Projekt gespeichert', project.name)
  } catch (e) {
    notify.error(e)
  } finally {
    busy.value = null
  }
}

async function addMissing(project: ProjectProposal) {
  busy.value = project.name
  try {
    const id = await save(project)
    const added = await api.projects.addMissingToShoppingList(id)
    notify.success('Einkaufsliste aktualisiert', `${added.length} Position(en) hinzugefügt`)
  } catch (e) {
    notify.error(e)
  } finally {
    busy.value = null
  }
}

async function ask() {
  saved.value = new Map()
  await store.ask()
  if (store.error) refreshLlmStatus()
}
</script>

<template>
  <section class="mx-auto flex max-w-4xl flex-col gap-4">
    <h1 class="text-2xl font-semibold">Projektassistent</h1>
    <LlmPausedNotice :status="llmStatus" />

    <Card>
      <template #content>
        <form class="flex flex-col gap-3" @submit.prevent="ask">
          <Textarea v-model="store.question" rows="3" auto-resize placeholder="Was möchtest du bauen?" />
          <div class="flex flex-wrap gap-2">
            <Button v-for="example in examples" :key="example" :label="example" size="small" severity="secondary" outlined @click="store.question = example" />
          </div>
          <Button type="submit" label="Fragen" icon="pi pi-sparkles" class="self-end" :loading="store.loading" :disabled="!store.question.trim() || llmStatus?.paused" />
          <div v-if="store.loading" class="flex flex-col gap-1">
            <ProgressBar mode="indeterminate" class="h-1" />
            <span class="text-sm text-surface-500">Das lokale Modell recherchiert im Lager und im Web. Das kann ein paar Minuten dauern, vor allem wenn es erst geladen werden muss.</span>
          </div>
        </form>
      </template>
    </Card>

    <Message v-if="store.error" severity="error">{{ store.error }}</Message>

    <template v-if="store.answer && !store.loading">
      <Message v-if="store.answer.projects.length === 0" severity="warn">
        Das Modell hat keine strukturierten Vorschläge geliefert. Die Recherche steht unten.
      </Message>

      <Card v-for="project in store.answer.projects" :key="project.name">
        <template #title>
          <div class="flex flex-wrap items-center justify-between gap-2">
            <span>{{ project.name }}</span>
            <div class="flex gap-2">
              <Tag :value="project.difficulty" severity="info" />
              <Tag
                :value="missingCount(project) === 0 ? 'Alles da' : `${missingCount(project)} Teil(e) fehlen`"
                :severity="missingCount(project) === 0 ? 'success' : 'warn'"
              />
            </div>
          </div>
        </template>
        <template #content>
          <div class="flex flex-col gap-3">
            <p>{{ project.description }}</p>
            <AvailabilityTable :parts="project.parts" />
            <div v-if="project.sources.length" class="flex flex-col gap-1 text-sm">
              <span class="font-medium">Quellen</span>
              <a v-for="source in project.sources" :key="source" :href="source" target="_blank" rel="noopener" class="truncate text-primary underline">
                {{ source }}
              </a>
            </div>
            <div class="flex flex-wrap justify-end gap-2">
              <Button
                label="Projekt speichern"
                icon="pi pi-bookmark"
                severity="secondary"
                :disabled="saved.has(project.name)"
                :loading="busy === project.name"
                @click="saveProject(project)"
              />
              <Button
                label="Fehlendes auf Einkaufsliste"
                icon="pi pi-cart-plus"
                :disabled="missingCount(project) === 0"
                :loading="busy === project.name"
                @click="addMissing(project)"
              />
            </div>
          </div>
        </template>
      </Card>

      <Panel header="Recherche des Modells" toggleable :collapsed="store.answer.projects.length > 0">
        <p class="whitespace-pre-wrap text-sm">{{ store.answer.research }}</p>
      </Panel>
    </template>
  </section>
</template>
