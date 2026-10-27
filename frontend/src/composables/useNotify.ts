import { useToast } from 'primevue/usetoast'

export function useNotify() {
  const toast = useToast()
  return {
    success: (summary: string, detail?: string) => toast.add({ severity: 'success', summary, detail, life: 3000 }),
    error: (e: unknown, summary = 'Fehler') =>
      toast.add({ severity: 'error', summary, detail: e instanceof Error ? e.message : String(e), life: 6000 }),
  }
}
