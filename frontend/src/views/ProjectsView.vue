<script setup lang="ts">
import Button from 'primevue/button'
import Card from 'primevue/card'
import { useConfirm } from 'primevue/useconfirm'
import { onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { api } from '../api/client'
import type { Project } from '../api/types'
import AvailabilityTable from '../components/AvailabilityTable.vue'
import { useNotify } from '../composables/useNotify'

const notify = useNotify()
const confirm = useConfirm()
const projects = ref<Project[]>([])
const loaded = ref(false)

async function load() {
  projects.value = await api.projects.list()
  loaded.value = true
}

async function addMissing(project: Project) {
  try {
    const added = await api.projects.addMissingToShoppingList(project.id)
    notify.success('Einkaufsliste aktualisiert', `${added.length} Position(en) hinzugefügt`)
  } catch (e) {
    notify.error(e)
  }
}

function remove(project: Project) {
  confirm.require({
    header: 'Projekt löschen?',
    message: project.name,
    acceptProps: { label: 'Löschen', severity: 'danger' },
    rejectProps: { label: 'Abbrechen', severity: 'secondary', text: true },
    accept: async () => {
      try {
        await api.projects.remove(project.id)
        projects.value = projects.value.filter((p) => p.id !== project.id)
      } catch (e) {
        notify.error(e)
      }
    },
  })
}

onMounted(() => load().catch(notify.error))
</script>

<template>
  <section class="mx-auto flex max-w-4xl flex-col gap-4">
    <h1 class="text-2xl font-semibold">Projekte</h1>
    <p v-if="loaded && projects.length === 0" class="text-surface-500">
      Noch keine gespeicherten Projekte. Frag den <RouterLink to="/assistent" class="underline">Projektassistenten</RouterLink>.
    </p>

    <Card v-for="project in projects" :key="project.id">
      <template #title>{{ project.name }}</template>
      <template #subtitle>{{ project.question }}</template>
      <template #content>
        <div class="flex flex-col gap-3">
          <p v-if="project.description">{{ project.description }}</p>
          <AvailabilityTable :parts="project.parts" />
          <div class="flex justify-end gap-2">
            <Button label="Löschen" icon="pi pi-trash" severity="danger" text @click="remove(project)" />
            <Button
              label="Fehlendes auf Einkaufsliste"
              icon="pi pi-cart-plus"
              :disabled="project.parts.every((p) => p.quantityMissing === 0)"
              @click="addMissing(project)"
            />
          </div>
        </div>
      </template>
    </Card>
  </section>
</template>
