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
