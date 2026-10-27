<script setup lang="ts">
import Message from 'primevue/message'
import { computed } from 'vue'
import type { LlmStatus } from '../api/types'

const props = defineProps<{ status: LlmStatus | null }>()

const since = computed(() =>
  props.status?.pausedSince
    ? new Date(props.status.pausedSince).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
    : null,
)
</script>

<template>
  <Message v-if="status?.paused" severity="warn" icon="pi pi-pause-circle">
    Das lokale LLM ist pausiert<template v-if="since"> (seit {{ since }})</template>, weil der Rechner gerade für Tonwerk gebraucht wird.
    Lager, Container und Einkaufsliste funktionieren weiter; Erkennung und Assistent gehen wieder, sobald die Pause vorbei ist.
  </Message>
</template>
