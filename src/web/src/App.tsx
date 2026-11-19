import { useTranslation } from 'react-i18next'
import { Button } from '@/components/ui/button'

export default function App() {
  const { t, i18n } = useTranslation()
  const nextLanguage = i18n.language === 'bn' ? 'en' : 'bn'

  return (
    <main className="mx-auto flex min-h-svh max-w-md flex-col items-center justify-center gap-4 p-4 text-center">
      <h1 className="text-3xl font-bold">{t('app.name')}</h1>
      <p className="text-muted-foreground">{t('app.tagline')}</p>
      <Button variant="outline" onClick={() => void i18n.changeLanguage(nextLanguage)}>
        {t('app.switchLanguage')}
      </Button>
    </main>
  )
}
