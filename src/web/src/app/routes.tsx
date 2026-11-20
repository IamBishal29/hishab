import type { RouteObject } from 'react-router'
import { LoginPage } from '@/features/auth/LoginPage'
import { ShopSetupPage } from '@/features/shops/ShopSetupPage'
import { AppShell, HomePage, RequireSession } from './AppShell'

export const routes: RouteObject[] = [
  { path: '/login', element: <LoginPage /> },
  {
    element: <RequireSession />,
    children: [
      {
        element: <AppShell />,
        children: [
          { index: true, element: <HomePage /> },
          { path: 'setup', element: <ShopSetupPage /> },
        ],
      },
    ],
  },
]
