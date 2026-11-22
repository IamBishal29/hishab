import { useMutation } from '@tanstack/react-query'
import { getApiOrdersLabels, type LabelFormat } from '@/api/generated/hishab'
import { saveFile } from '@/lib/download'

/**
 * Downloads one PDF of labels. A mutation, not a query: printing is an action the user starts,
 * and a cached PDF would go stale as soon as an order changes.
 */
export function useDownloadLabels() {
  return useMutation({
    mutationFn: async ({ ids, format }: { ids: string[]; format: LabelFormat }) => {
      const pdf = await getApiOrdersLabels({ Ids: ids, Format: format })
      saveFile(pdf, `labels-${format.toLowerCase()}.pdf`)
    },
  })
}
