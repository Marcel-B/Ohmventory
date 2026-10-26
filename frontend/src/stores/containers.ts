import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '../api/client'
import { containerTypeLabels, type Container, type ContainerFields } from '../api/types'

export const useContainersStore = defineStore('containers', () => {
  const containers = ref<Container[]>([])
  const loading = ref(false)

  const options = computed(() =>
    containers.value.map((c) => ({
      label: c.name ? `${c.code} · ${c.name}` : `${c.code} (${containerTypeLabels[c.type]})`,
      value: c.id,
    })),
  )

  async function load() {
    loading.value = true
    try {
      containers.value = await api.containers.list()
    } finally {
      loading.value = false
    }
  }

  async function create(fields: ContainerFields) {
    const container = await api.containers.create(fields)
    await load()
    return container
  }

  async function update(id: string, fields: ContainerFields) {
    const container = await api.containers.update(id, fields)
    await load()
    return container
  }

  async function remove(id: string) {
    await api.containers.remove(id)
    containers.value = containers.value.filter((c) => c.id !== id)
  }

  return { containers, loading, options, load, create, update, remove }
})
