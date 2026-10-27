<script setup lang="ts">
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import { useConfirm } from 'primevue/useconfirm'
import { computed, onMounted, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { containerTypeLabels, containerTypes, type Container, type ContainerFields } from '../api/types'
import { printLabels } from '../composables/printLabels'
import { useNotify } from '../composables/useNotify'
import { useContainersStore } from '../stores/containers'

const store = useContainersStore()
const notify = useNotify()
const confirm = useConfirm()

const typeOptions = containerTypes.map((value) => ({ value, label: containerTypeLabels[value] }))

const editing = ref<{ id: string | null; fields: ContainerFields } | null>(null)
const saving = ref(false)

const parentOptions = computed(() => [
  { label: 'Keiner', value: null },
  ...store.options.filter((o) => o.value !== editing.value?.id),
])

function create() {
  editing.value = { id: null, fields: { code: null, name: null, type: 'Tray', description: null, parentId: null } }
}

function edit(c: Container) {
  editing.value = { id: c.id, fields: { code: c.code, name: c.name, type: c.type, description: c.description, parentId: c.parentId } }
}

async function save() {
  if (!editing.value) return
  saving.value = true
  try {
    const { id, fields } = editing.value
    const container = id ? await store.update(id, fields) : await store.create(fields)
    notify.success(id ? 'Container gespeichert' : `Container ${container.code} angelegt`)
    editing.value = null
  } catch (e) {
    notify.error(e)
  } finally {
    saving.value = false
  }
}

function remove(c: Container) {
  confirm.require({
    header: `${c.code} löschen?`,
    message: 'Nur leere Container ohne Unter-Container können gelöscht werden.',
    acceptProps: { label: 'Löschen', severity: 'danger' },
    rejectProps: { label: 'Abbrechen', severity: 'secondary', text: true },
    accept: async () => {
      try {
        await store.remove(c.id)
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
      <h1 class="text-2xl font-semibold">Container</h1>
      <div class="flex gap-2">
        <Button
          label="Etiketten drucken"
          icon="pi pi-print"
          severity="secondary"
          :disabled="store.containers.length === 0"
          @click="printLabels(store.containers)"
        />
        <Button label="Neuer Container" icon="pi pi-plus" @click="create" />
      </div>
    </div>

    <p v-if="!store.loading && store.containers.length === 0" class="text-surface-500">
      Noch keine Container. Lege z. B. deine Catch-all-Trays an; der Code (T-001, T-002 …) wird automatisch vergeben.
    </p>

    <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
      <div
        v-for="c in store.containers"
        :key="c.id"
        class="flex flex-col gap-2 rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="flex items-start justify-between gap-2">
          <RouterLink :to="`/containers/${c.code}`" class="flex flex-col">
            <span class="font-mono text-lg font-semibold">{{ c.code }}</span>
            <span class="text-sm text-surface-500">{{ c.name ?? containerTypeLabels[c.type] }}</span>
          </RouterLink>
          <Tag :value="`${c.partCount} ${c.partCount === 1 ? 'Teil' : 'Teile'}`" severity="secondary" />
        </div>
        <div class="flex items-center justify-between text-sm text-surface-500">
          <span>{{ containerTypeLabels[c.type] }}<template v-if="c.parentCode"> in {{ c.parentCode }}</template></span>
          <div>
            <Button icon="pi pi-print" text rounded size="small" aria-label="Etikett" @click="printLabels([c])" />
            <Button icon="pi pi-pencil" text rounded size="small" severity="secondary" aria-label="Bearbeiten" @click="edit(c)" />
            <Button icon="pi pi-trash" text rounded size="small" severity="danger" aria-label="Löschen" @click="remove(c)" />
          </div>
        </div>
      </div>
    </div>

    <Dialog
      :visible="editing !== null"
      modal
      :header="editing?.id ? 'Container bearbeiten' : 'Neuer Container'"
      class="w-full max-w-lg"
      @update:visible="editing = null"
    >
      <div v-if="editing" class="grid gap-4 sm:grid-cols-2">
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">Typ</span>
          <Select v-model="editing.fields.type" :options="typeOptions" option-label="label" option-value="value" />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">Code</span>
          <InputText v-model="editing.fields.code" placeholder="automatisch" class="font-mono uppercase" />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">Name</span>
          <InputText v-model="editing.fields.name" placeholder="z. B. Sensoren" />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-sm font-medium">Liegt in</span>
          <Select v-model="editing.fields.parentId" :options="parentOptions" option-label="label" option-value="value" filter />
        </label>
        <label class="flex flex-col gap-1 sm:col-span-2">
          <span class="text-sm font-medium">Beschreibung</span>
          <Textarea v-model="editing.fields.description" rows="2" auto-resize />
        </label>
      </div>
      <template #footer>
        <Button label="Abbrechen" severity="secondary" text @click="editing = null" />
        <Button label="Speichern" icon="pi pi-check" :loading="saving" @click="save" />
      </template>
    </Dialog>
  </section>
</template>
