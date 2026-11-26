import { writeFileSync } from 'node:fs'
import gifenc from 'gifenc' // CommonJS package: no named ESM exports
import { PNG } from 'pngjs'

export interface Frame {
  png: Buffer
  /** How long the frame stays on screen. */
  ms: number
}

/** Same-size PNG screenshots -> an animated GIF (256 colours per frame: enough for a flat UI). */
export function writeGif(path: string, frames: Frame[]): void {
  const { GIFEncoder, applyPalette, quantize } = gifenc
  const gif = GIFEncoder()
  for (const frame of frames) {
    const { data, width, height } = PNG.sync.read(frame.png)
    const palette = quantize(data, 256)
    gif.writeFrame(applyPalette(data, palette), width, height, { palette, delay: frame.ms })
  }
  gif.finish()
  writeFileSync(path, gif.bytes())
}
