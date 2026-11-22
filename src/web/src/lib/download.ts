/**
 * Saves a file the API returned (labels PDF). The PDF arrives after an await, and browsers block
 * `window.open` that is not directly inside a click (popup blockers, most phones), but a
 * `download` link click still works. The object URL is revoked afterwards so the PDF does not
 * stay in memory for the life of the tab.
 */
export function saveFile(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.append(link)
  link.click()
  link.remove()
  // Browsers read the URL after click() returns (Firefox can take a moment): revoke a bit later.
  setTimeout(() => {
    URL.revokeObjectURL(url)
  }, 10_000)
}
