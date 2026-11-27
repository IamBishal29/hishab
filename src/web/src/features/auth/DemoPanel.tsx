import type { ReactElement } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'
import { useDemo, useDemoSignIn } from './demo'

/** "Try the demo" on the login page: shown only when the API runs as the hosted demo. */
export function DemoPanel(): ReactElement | null {
  const { t } = useTranslation()
  const demo = useDemo()
  const signIn = useDemoSignIn()
  if (!demo || demo.roles.length === 0) return null

  const [owner, ...staff] = demo.roles
  return (
    <section
      aria-labelledby="demo-title"
      className="flex flex-col gap-3 rounded-md border border-dashed p-4"
    >
      <h2 id="demo-title" className="font-semibold">
        {t('demo.title')}
      </h2>
      <p className="text-sm text-muted-foreground">{t('demo.hint')}</p>
      {owner && (
        <Button
          disabled={signIn.isPending}
          onClick={() => {
            signIn.mutate(owner)
          }}
        >
          {t('demo.signInAs', { role: t(`roles.${owner}`) })}
        </Button>
      )}
      {staff.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">{t('demo.otherRoles')}</span>
          {staff.map((role) => (
            <Button
              key={role}
              variant="outline"
              size="sm"
              disabled={signIn.isPending}
              onClick={() => {
                signIn.mutate(role)
              }}
            >
              {t(`roles.${role}`)}
            </Button>
          ))}
        </div>
      )}
      {signIn.isError && (
        <p role="alert" className="text-sm text-destructive">
          {t('demo.failed')}
        </p>
      )}
    </section>
  )
}

/** On every signed-in page of the hosted demo, so nobody mistakes it for a real shop. */
export function DemoBanner(): ReactElement | null {
  const { t } = useTranslation()
  if (!useDemo()) return null
  return (
    <p role="note" className="bg-amber-100 px-4 py-1 text-center text-sm text-amber-950">
      {t('demo.banner')}
    </p>
  )
}
