import { vi } from 'vitest'

export interface Download {
  name: string
  blob: Blob
}

/**
 * Records files the app saves (lib/download.ts) instead of letting jsdom try to navigate to a
 * blob: URL. The spies are restored after each test (test/setup.ts).
 */
export function captureDownloads(): Download[] {
  const downloads: Download[] = []
  const blobs = new Map<string, Blob>()
  vi.spyOn(URL, 'createObjectURL').mockImplementation((blob) => {
    const url = `blob:test/${String(blobs.size)}`
    blobs.set(url, blob as Blob)
    return url
  })
  vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined)
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
    this: HTMLAnchorElement,
  ) {
    const blob = blobs.get(this.href)
    if (blob) downloads.push({ name: this.download, blob })
  })
  return downloads
}
