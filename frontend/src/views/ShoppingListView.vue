<script setup lang="ts">
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import InputNumber from 'primevue/inputnumber'
import InputText from 'primevue/inputtext'
import { onMounted, ref } from 'vue'
import { useNotify } from '../composables/useNotify'
import { useShoppingListStore } from '../stores/shoppingList'

const store = useShoppingListStore()
const notify = useNotify()

const name = ref('')
const quantity = ref(1)

async function add() {
  if (!name.value.trim()) return
  try {
    await store.add({ name: name.value.trim(), partNumber: null, quantity: quantity.value, url: null, done: false })
    name.value = ''
    quantity.value = 1
  } catch (e) {
    notify.error(e)
  }
}

function copyOpen() {
  const text = store.items
    .filter((i) => !i.done)
    .map((i) => `${i.quantity}x ${i.name}${i.partNumber && !i.name.includes(i.partNumber) ? ` (${i.partNumber})` : ''}`)
    .join('\n')
  navigator.clipboard.writeText(text).then(() => notify.success('In die Zwischenablage kopiert'), notify.error)
}

onMounted(() => store.load().catch(notify.error))
</script>

<template>
  <section class="mx-auto flex max-w-3xl flex-col gap-4">
    <div class="flex items-center justify-between gap-3">
      <h1 class="text-2xl font-semibold">Einkaufsliste</h1>
      <Button label="Kopieren" icon="pi pi-copy" severity="secondary" :disabled="store.openCount === 0" @click="copyOpen" />
    </div>

    <form class="flex gap-2" @submit.prevent="add">
      <InputNumber v-model="quantity" :min="1" input-class="w-16 text-center" />
      <InputText v-model="name" placeholder="Bauteil hinzufügen …" class="min-w-0 flex-1" />
      <Button type="submit" icon="pi pi-plus" aria-label="Hinzufügen" />
    </form>

    <p v-if="store.items.length === 0" class="text-surface-500">Die Einkaufsliste ist leer.</p>

    <ul class="flex flex-col divide-y divide-surface-200 rounded-xl border border-surface-200 bg-surface-0 dark:divide-surface-800 dark:border-surface-800 dark:bg-surface-900">
      <li v-for="item in store.items" :key="item.id" class="flex items-center gap-3 p-3">
        <Checkbox :model-value="item.done" binary @update:model-value="store.update(item, { done: $event }).catch(notify.error)" />
        <div class="flex-1" :class="{ 'text-surface-400 line-through': item.done }">
          <div>{{ item.quantity }}× {{ item.name }}</div>
          <div class="text-xs text-surface-500">
            {{ [item.partNumber, item.projectName ? `für ${item.projectName}` : null].filter(Boolean).join(' · ') }}
          </div>
        </div>
        <Button icon="pi pi-trash" text rounded severity="secondary" aria-label="Entfernen" @click="store.remove(item.id).catch(notify.error)" />
      </li>
    </ul>
  </section>
</template>
