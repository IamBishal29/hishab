import { useMutation } from '@tanstack/react-query'
import { useId, useState, type ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { postApiOrdersDraftsFromChat, type OrderDraft } from '@/api/generated/hishab'
import { Button } from '@/components/ui/button'
import { problemMessage } from '@/lib/problemErrors'

export const chatMaxLength = 4000

/** The mark on a field the assistant filled: words and a symbol, never colour alone. */
export function SuggestedBadge(): ReactElement {
  const { t } = useTranslation()
  return (
    <span className="ml-2 inline-flex items-center gap-1 rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-950">
      <span aria-hidden="true">✦</span>
      {t('order.draft.suggested')}
    </span>
  )
}

/**
 * Paste a buyer chat and let the assistant fill the order form. It only suggests: the seller sees
 * every filled field marked, the items it could not match, and its warnings, and still presses Save.
 */
export function ChatDraftPanel({
  onDraft,
}: {
  onDraft: (draft: OrderDraft) => void
}): ReactElement {
  const { t } = useTranslation()
  const id = useId()
  const [chat, setChat] = useState('')
  const draft = useMutation({
    mutationFn: (text: string) => postApiOrdersDraftsFromChat({ chat: text }),
    onSuccess: onDraft,
  })
  const error = draft.isError ? problemMessage(draft.error) : null

  return (
    <details className="rounded-md border p-3">
      <summary className="cursor-pointer text-sm font-medium">{t('order.draft.open')}</summary>
      <div className="mt-3 flex flex-col gap-2">
        <label htmlFor={id} className="text-sm font-medium">
          {t('order.draft.chatLabel')}
        </label>
        <textarea
          id={id}
          rows={5}
          maxLength={chatMaxLength}
          value={chat}
          aria-describedby={`${id}-hint`}
          className="rounded-md border border-input bg-transparent px-3 py-2 text-base md:text-sm"
          onChange={(e) => {
            setChat(e.target.value)
          }}
        />
        <p id={`${id}-hint`} className="text-xs text-muted-foreground">
          {t('order.draft.hint', { count: chat.length, max: chatMaxLength })}
        </p>
        <Button
          type="button"
          variant="secondary"
          disabled={chat.trim() === '' || draft.isPending}
          onClick={() => {
            draft.mutate(chat)
          }}
        >
          {draft.isPending ? t('order.draft.reading') : t('order.draft.fill')}
        </Button>
        {error && (
          <p role="alert" className="text-sm text-destructive">
            {t(error, { defaultValue: error })}
          </p>
        )}
        {draft.isSuccess && <DraftOutcome draft={draft.data} />}
      </div>
    </details>
  )
}

function DraftOutcome({ draft }: { draft: OrderDraft }): ReactElement {
  const { t } = useTranslation()
  const unmatched = draft.lines.filter((l) => l.variantId === null)
  return (
    <div
      role="status"
      className="flex flex-col gap-2 rounded-md bg-amber-50 p-3 text-sm text-amber-950"
    >
      <p className="font-medium">{t('order.draft.filled')}</p>
      {unmatched.length > 0 && (
        <div>
          <p>{t('order.draft.unmatched')}</p>
          <ul className="list-disc pl-5">
            {unmatched.map((line, index) => (
              <li key={index}>
                {t('order.draft.unmatchedLine', {
                  description: line.description,
                  quantity: line.quantity,
                })}
              </li>
            ))}
          </ul>
        </div>
      )}
      {draft.warnings.filter((w) => w !== 'unmatchedItem').length > 0 && (
        <ul className="list-disc pl-5">
          {draft.warnings
            .filter((w) => w !== 'unmatchedItem') // already shown as the list above
            .map((code) => (
              <li key={code}>
                {t(`order.draft.warnings.${code}`, {
                  defaultValue: t('order.draft.warnings.other'),
                })}
              </li>
            ))}
        </ul>
      )}
    </div>
  )
}
