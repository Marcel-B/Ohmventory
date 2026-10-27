import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '../api/client'
import type { ShoppingListFields, ShoppingListItem } from '../api/types'

export const useShoppingListStore = defineStore('shoppingList', () => {
  const items = ref<ShoppingListItem[]>([])
  const openCount = computed(() => items.value.filter((i) => !i.done).length)

  async function load() {
    items.value = await api.shoppingList.list()
  }

  async function add(fields: ShoppingListFields) {
    items.value.unshift(await api.shoppingList.create(fields))
  }

  async function update(item: ShoppingListItem, changes: Partial<ShoppingListFields>) {
    const updated = await api.shoppingList.update(item.id, { ...item, ...changes })
    items.value = items.value.map((i) => (i.id === updated.id ? updated : i))
  }

  async function remove(id: string) {
    await api.shoppingList.remove(id)
    items.value = items.value.filter((i) => i.id !== id)
  }

  return { items, openCount, load, add, update, remove }
})
