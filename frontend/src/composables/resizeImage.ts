/**
 * Verkleinert Handyfotos vor dem Upload. Für die Erkennung reichen ~1600 px Kantenlänge,
 * und das lokale Vision-Modell antwortet bei kleineren Bildern deutlich schneller.
 */
export async function resizeImage(file: File, maxSize = 1600, quality = 0.88): Promise<Blob> {
  const bitmap = await createImageBitmap(file)
  const scale = Math.min(1, maxSize / Math.max(bitmap.width, bitmap.height))
  if (scale === 1 && file.type === 'image/jpeg') {
    bitmap.close()
    return file
  }

  const canvas = document.createElement('canvas')
  canvas.width = Math.round(bitmap.width * scale)
  canvas.height = Math.round(bitmap.height * scale)
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  bitmap.close()

  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Bild konnte nicht verarbeitet werden.'))), 'image/jpeg', quality),
  )
}

/**
 * Dreht ein Bild um 90° (im oder gegen den Uhrzeigersinn). Die Erkennung bekommt so senkrecht gedruckten Text
 * auf Platinen (z. B. „1.30" IIC V2.2") auch waagerecht zu sehen. Gedrehte Ansichten bleiben kleiner, sie dienen nur dem Lesen.
 */
export async function rotateImage(image: Blob, clockwise: boolean, maxSize = 1200, quality = 0.85): Promise<Blob> {
  const bitmap = await createImageBitmap(image)
  const scale = Math.min(1, maxSize / Math.max(bitmap.width, bitmap.height))
  const width = Math.round(bitmap.width * scale)
  const height = Math.round(bitmap.height * scale)

  const canvas = document.createElement('canvas')
  canvas.width = height
  canvas.height = width
  const context = canvas.getContext('2d')!
  context.translate(height / 2, width / 2)
  context.rotate(((clockwise ? 1 : -1) * Math.PI) / 2)
  context.drawImage(bitmap, -width / 2, -height / 2, width, height)
  bitmap.close()

  return new Promise((resolve, reject) =>
    canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('Bild konnte nicht gedreht werden.'))), 'image/jpeg', quality),
  )
}
