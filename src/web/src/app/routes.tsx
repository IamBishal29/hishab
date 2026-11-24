import type { RouteObject } from 'react-router'
import { LoginPage } from '@/features/auth/LoginPage'
import { CourierSettingsPage } from '@/features/couriers/CourierSettingsPage'
import { CustomerPage } from '@/features/customers/CustomerPage'
import { CodSummaryPage } from '@/features/money/CodSummaryPage'
import { PayoutPage } from '@/features/money/PayoutPage'
import { RecordPayoutPage } from '@/features/money/RecordPayoutPage'
import { OrderDetailPage } from '@/features/orders/OrderDetailPage'
import { OrderListPage } from '@/features/orders/OrderListPage'
import { QuickOrderPage } from '@/features/orders/QuickOrderPage'
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
          { path: 'orders', element: <OrderListPage /> },
          { path: 'orders/new', element: <QuickOrderPage /> },
          { path: 'orders/:id', element: <OrderDetailPage /> },
          { path: 'customers/:id', element: <CustomerPage /> },
          { path: 'settings/couriers', element: <CourierSettingsPage /> },
          { path: 'money', element: <CodSummaryPage /> },
          { path: 'money/payouts/new', element: <RecordPayoutPage /> },
          { path: 'money/payouts/:id', element: <PayoutPage /> },
        ],
      },
    ],
  },
]
