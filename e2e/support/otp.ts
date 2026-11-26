import { execFileSync } from 'node:child_process'

/**
 * The API runs with Otp:Delivery=Log (no SMS gateway yet): the code is in its JSON log with the phone
 * masked ("01000****01"). Reads the API container's log; E2E_API_LOGS overrides the command (e.g. CI).
 */
function apiLog(): string {
  const command =
    process.env.E2E_API_LOGS ?? 'docker compose --profile app logs api --no-color --no-log-prefix'
  const [file, ...args] = command.split(' ')
  return execFileSync(file ?? 'docker', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
}

/** Local "01000000001" -> the masked form the API logs ("01000****01"). */
export function maskedPhone(local: string): string {
  return `${local.slice(0, 5)}****${local.slice(-2)}`
}

/** All codes logged for this phone so far, oldest first. */
export function otpCodes(local: string): string[] {
  const masked = maskedPhone(local)
  const codes: string[] = []
  for (const line of apiLog().split('\n')) {
    if (!line.includes(masked)) continue
    try {
      const event = JSON.parse(line) as { Code?: unknown; Phone?: unknown }
      if (event.Phone === masked && typeof event.Code === 'string') codes.push(event.Code)
    } catch {
      // not a JSON log line (startup banner): skip
    }
  }
  return codes
}

/** Waits for a code newer than the ones seen before the request (no fixed sleep). */
export async function nextOtp(
  local: string,
  seenBefore: number,
  timeoutMs = 15_000,
): Promise<string> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const codes = otpCodes(local)
    const latest = codes.at(-1)
    if (codes.length > seenBefore && latest) return latest
    await new Promise((resolve) => setTimeout(resolve, 250))
  }
  throw new Error(
    `No new OTP for ${maskedPhone(local)} in the API log within ${String(timeoutMs)} ms`,
  )
}
