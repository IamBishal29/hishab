import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, Navigate, Outlet, useLocation, useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { endSession, useSession } from '@/features/auth/useSession'

/** Protected routes: wait for the session (a reload restores it from the refresh cookie), else go to login. */
export function RequireSession(): ReactElement {
  const { t } = useTranslation()
  const session = useSession()
  const location = useLocation()

  if (session.isPending) return <p className="p-4 text-center">{t('common.loading')}</p>
  if (!session.data) return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet />
}

/** Mobile-first frame: a compact header and one column that fits 360 px. */
export function AppShell(): ReactElement {
  const { t, i18n } = useTranslation()
  const navigate = useNavigate()
  const nextLanguage = i18n.language === 'bn' ? 'en' : 'bn'

  return (
    <div className="flex min-h-svh flex-col">
      <header className="flex items-center justify-between gap-2 border-b px-4 py-2">
        <span className="text-lg font-bold">{t('app.name')}</span>
        <nav className="flex items-center gap-1">
          <Button variant="ghost" size="sm" onClick={() => void i18n.changeLanguage(nextLanguage)}>
            {t('app.switchLanguage')}
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => void endSession().then(() => navigate('/login', { replace: true }))}
          >
            {t('shell.logout')}
          </Button>
        </nav>
      </header>
      <main className="flex-1 p-4">
        <Outlet />
      </main>
    </div>
  )
}

// Mirrors the API's EnterOrders permission; the API still decides (403 for anyone else).
const canEnterOrders = new Set<string>(['Owner', 'Manager', 'Moderator'])

export function HomePage(): ReactElement {
  const { t } = useTranslation()
  const { data: session } = useSession()
  return (
    <section className="mx-auto max-w-md">
      <h1 className="text-2xl font-bold">{t('home.title')}</h1>
      <p className="text-muted-foreground">{t('home.signedInAs', { role: session?.role ?? '' })}</p>
      {session && canEnterOrders.has(session.role) && (
        <Button asChild className="mt-4">
          <Link to="/orders/new">{t('home.newOrder')}</Link>
        </Button>
      )}
    </section>
  )
}
