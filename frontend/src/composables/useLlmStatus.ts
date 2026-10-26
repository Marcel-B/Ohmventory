import { onMounted, onUnmounted, ref } from 'vue'
import { api } from '../api/client'
import type { LlmStatus } from '../api/types'

/**
 * Fragt regelmäßig ab, ob das LLM pausiert ist (yue-guard hält es an, wenn Tonwerk arbeitet),
 * damit Erkennung und Assistent das anzeigen statt erst nach dem Klick zu scheitern.
 */
export function useLlmStatus(intervalMs = 30_000) {
  const status = ref<LlmStatus | null>(null)
  let timer: ReturnType<typeof setInterval> | undefined

  async function refresh() {
    try {
      status.value = await api.llm.status()
    } catch {
      status.value = null
    }
  }

  onMounted(() => {
    refresh()
    timer = setInterval(refresh, intervalMs)
  })
  onUnmounted(() => clearInterval(timer))

  return { status, refresh }
}
