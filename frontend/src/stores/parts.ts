import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '../api/client'
import type { Part, PartFields } from '../api/types'

export const usePartsStore = defineStore('parts', () => {
  const parts = ref<Part[]>([])
  const loading = ref(false)

  async function load() {
    loading.value = true
    try {
      parts.value = await api.parts.list()
    } finally {
      loading.value = false
    }
  }

  function upsert(part: Part) {
    const index = parts.value.findIndex((p) => p.id === part.id)
    if (index >= 0) parts.value[index] = part
    else parts.value.push(part)
    parts.value.sort((a, b) => a.name.localeCompare(b.name, 'de'))
  }

  async function create(fields: PartFields, quantity: number, containerId: string | null, imageId: string | null) {
    const part = await api.parts.create(fields, quantity, containerId, imageId)
    upsert(part)
    return part
  }

  async function update(id: string, fields: PartFields) {
    const part = await api.parts.update(id, fields)
    upsert(part)
    return part
  }

  async function setStock(id: string, containerId: string | null, quantity: number) {
    const part = await api.parts.setStock(id, containerId, quantity)
    upsert(part)
    return part
  }

  async function remove(id: string) {
    await api.parts.remove(id)
    parts.value = parts.value.filter((p) => p.id !== id)
  }

  return { parts, loading, load, create, update, setStock, remove }
})
