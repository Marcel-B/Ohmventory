import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api } from '../api/client'
import type { AssistantAnswer } from '../api/types'

// Hält die letzte Antwort, damit sie beim Wechsel zwischen den Ansichten nicht verloren geht.
export const useAssistantStore = defineStore('assistant', () => {
  const question = ref('')
  const answer = ref<AssistantAnswer | null>(null)
  const loading = ref(false)
  const error = ref<string | null>(null)

  async function ask() {
    loading.value = true
    error.value = null
    try {
      answer.value = await api.assistant.ask(question.value)
    } catch (e) {
      error.value = e instanceof Error ? e.message : String(e)
    } finally {
      loading.value = false
    }
  }

  return { question, answer, loading, error, ask }
})
