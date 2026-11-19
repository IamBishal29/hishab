import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import App from './App'
import '@/i18n'

describe('App', () => {
  it('shows the app name and switches to Bangla', async () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: 'Hishab' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'বাংলা' }))

    expect(screen.getByRole('heading', { name: 'হিসাব' })).toBeInTheDocument()
  })
})
