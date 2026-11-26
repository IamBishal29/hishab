import { zodResolver } from '@hookform/resolvers/zod'
import { useState, type ReactElement } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { Navigate, useLocation } from 'react-router'
import { z } from 'zod'
import { usePostApiAuthOtp, usePostApiAuthOtpVerify } from '@/api/generated/hishab'
import { FormField } from '@/components/FormField'
import { Button } from '@/components/ui/button'
import { bdMobilePattern } from '@/lib/phone'
import { applyProblem } from '@/lib/problemErrors'
import { startSession, useSession } from './useSession'

const phoneSchema = z.object({
  phone: z.string().trim().regex(bdMobilePattern, 'login.phoneInvalid'),
})
const codeSchema = z.object({
  code: z
    .string()
    .trim()
    .regex(/^\d{6}$/, 'login.codeInvalid'),
})

type PhoneForm = z.infer<typeof phoneSchema>
type CodeForm = z.infer<typeof codeSchema>

export function LoginPage(): ReactElement {
  const { t, i18n } = useTranslation()
  const { data: session } = useSession()
  const location = useLocation()
  const [phone, setPhone] = useState<string | null>(null)
  const [isNewShop, setIsNewShop] = useState(false)
  const from = (location.state as { from?: string } | null)?.from ?? '/'
  const afterLogin = isNewShop ? '/setup' : from

  const requestOtp = usePostApiAuthOtp()
  const verifyOtp = usePostApiAuthOtpVerify()

  const phoneForm = useForm<PhoneForm>({
    resolver: zodResolver(phoneSchema),
    defaultValues: { phone: '' },
  })
  const codeForm = useForm<CodeForm>({
    resolver: zodResolver(codeSchema),
    defaultValues: { code: '' },
  })

  // The only navigation away from login: as soon as a session exists, go to the setup page
  // for a brand-new shop, else back to where the user was heading.
  if (session) return <Navigate to={afterLogin} replace />

  const sendCode = phoneForm.handleSubmit(async (values) => {
    try {
      await requestOtp.mutateAsync({ data: { phone: values.phone } })
      setPhone(values.phone)
    } catch (error) {
      applyProblem(error, phoneForm.setError, ['phone'])
    }
  })

  const verify = codeForm.handleSubmit(async (values) => {
    if (!phone) return
    try {
      const login = await verifyOtp.mutateAsync({ data: { phone, code: values.code } })
      setIsNewShop(login.isNewShop)
      startSession(login) // the session redirect above takes over
    } catch (error) {
      applyProblem(error, codeForm.setError, ['code'])
    }
  })

  const rootError = (phone ? codeForm : phoneForm).formState.errors.root?.message

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-sm flex-col justify-center gap-6 p-4">
      <header className="text-center">
        <h1 className="text-3xl font-bold">{t('app.name')}</h1>
        <p className="text-muted-foreground">{t('login.subtitle')}</p>
        <Button
          variant="link"
          size="sm"
          onClick={() => void i18n.changeLanguage(i18n.language === 'bn' ? 'en' : 'bn')}
        >
          {t('app.switchLanguage')}
        </Button>
      </header>

      {rootError && (
        <p role="alert" className="rounded-md bg-destructive/10 p-3 text-sm text-destructive">
          {t(rootError, { defaultValue: rootError })}
        </p>
      )}

      {phone === null ? (
        <form onSubmit={(e) => void sendCode(e)} noValidate className="flex flex-col gap-4">
          <FormField
            label={t('login.phone')}
            type="tel"
            inputMode="tel"
            autoComplete="tel"
            placeholder={t('login.phonePlaceholder')}
            error={phoneForm.formState.errors.phone?.message}
            {...phoneForm.register('phone')}
          />
          <Button type="submit" disabled={phoneForm.formState.isSubmitting}>
            {t('login.sendCode')}
          </Button>
        </form>
      ) : (
        <form onSubmit={(e) => void verify(e)} noValidate className="flex flex-col gap-4">
          <p className="text-sm text-muted-foreground">{t('login.codeSent', { phone })}</p>
          <FormField
            label={t('login.code')}
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={6}
            error={codeForm.formState.errors.code?.message}
            {...codeForm.register('code')}
          />
          <Button type="submit" disabled={codeForm.formState.isSubmitting}>
            {t('login.verify')}
          </Button>
          <Button
            type="button"
            variant="link"
            onClick={() => {
              codeForm.reset()
              setPhone(null)
            }}
          >
            {t('login.changePhone')}
          </Button>
        </form>
      )}
    </main>
  )
}
