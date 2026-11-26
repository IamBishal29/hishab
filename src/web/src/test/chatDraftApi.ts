import { http, HttpResponse } from 'msw'
import type { OrderDraft } from '@/api/generated/hishab'

/** The fake chat-to-order API: tests set the next answer and inspect the chats sent. */
export const chatDraft = {
  /** The next POST /api/orders/drafts/from-chat answer: a draft, or a ProblemDetails status. */
  next: emptyDraft() as OrderDraft | 503 | 429,
  /** Every chat posted, in order. */
  chats: [] as string[],
}

export function emptyDraft(overrides: Partial<OrderDraft> = {}): OrderDraft {
  return {
    phone: null,
    customerName: null,
    deliveryAddress: null,
    isInsideDhaka: null,
    lines: [],
    note: null,
    warnings: [],
    ...overrides,
  }
}

export function resetChatDraft(): void {
  chatDraft.next = emptyDraft()
  chatDraft.chats = []
}

const problems = {
  503: 'The assistant did not answer. Try again in a moment, or enter the order by hand.',
  429: 'Your shop has drafted many orders this minute. Please wait a minute and try again.',
}

export const chatDraftHandlers = [
  http.post('*/api/orders/drafts/from-chat', async ({ request }) => {
    const body = (await request.json()) as { chat: string }
    chatDraft.chats.push(body.chat)
    const next = chatDraft.next
    if (typeof next === 'number') {
      return HttpResponse.json(
        { status: next, title: 'Assistant unavailable', detail: problems[next] },
        { status: next, headers: { 'Content-Type': 'application/problem+json' } },
      )
    }
    return HttpResponse.json(next)
  }),
]
