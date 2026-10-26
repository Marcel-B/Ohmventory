import { api } from '../api/client'
import type { Container } from '../api/types'

const escape = (value: string) => value.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]!)

/** Öffnet ein Druckfenster mit QR-Etiketten (ca. 30 mm), passend für Catch-all-Trays. */
export function printLabels(containers: Container[]) {
  const labels = containers
    .map(
      (c) => `
      <div class="label">
        <img src="${location.origin}${api.containers.labelUrl(c.code)}" alt="">
        <div class="code">${escape(c.code)}</div>
        ${c.name ? `<div class="name">${escape(c.name)}</div>` : ''}
      </div>`,
    )
    .join('')

  const win = window.open('', '_blank')
  if (!win) return
  win.document.write(`<!doctype html><html><head><title>Etiketten</title><style>
    body { font-family: system-ui, sans-serif; margin: 8mm; }
    .sheet { display: flex; flex-wrap: wrap; gap: 4mm; }
    .label { width: 30mm; border: 0.2mm dashed #999; padding: 2mm; text-align: center; break-inside: avoid; }
    .label img { width: 26mm; height: 26mm; }
    .code { font-weight: 700; font-size: 11pt; }
    .name { font-size: 7pt; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
  </style></head><body><div class="sheet">${labels}</div>
  <script>Promise.all([...document.images].map(i => i.decode().catch(() => {}))).then(() => print())<\/script>
  </body></html>`)
  win.document.close()
}
