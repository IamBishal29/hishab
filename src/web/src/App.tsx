import { QueryClientProvider } from '@tanstack/react-query'
import type { ReactElement } from 'react'
import { createBrowserRouter, RouterProvider } from 'react-router'
import { routes } from '@/app/routes'
import { queryClient } from '@/lib/queryClient'

type Router = ReturnType<typeof createBrowserRouter> // memory routers have the same type

const browserRouter = createBrowserRouter(routes)

/** Tests pass a memory router; the app uses the browser router. */
export default function App({ router = browserRouter }: { router?: Router }): ReactElement {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}
