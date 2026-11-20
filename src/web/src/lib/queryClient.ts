import { QueryClient } from '@tanstack/react-query'
import { ApiProblem } from '@/api/http'

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      // Retrying a 4xx (validation, 401, 403, 404) cannot succeed; only retry network/5xx.
      retry: (failureCount, error) =>
        !(error instanceof ApiProblem && error.status < 500) && failureCount < 2,
    },
  },
})
