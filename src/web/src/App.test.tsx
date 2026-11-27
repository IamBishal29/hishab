import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { routes } from '@/app/routes'
import { api, validCode } from '@/test/server'
import App from './App'

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  render(<App router={router} />)
  return router
}

async function signIn(code: string) {
  await userEvent.type(await screen.findByLabelText('Mobile number'), '01712345678')
  await userEvent.click(screen.getByRole('button', { name: 'Send code' }))
  await userEvent.type(await screen.findByLabelText('6-digit code'), code)
  await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
}

describe('App', () => {
  it('sends a signed-out visitor to the login page', async () => {
    const router = renderAt('/')

    expect(await screen.findByLabelText('Mobile number')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })

  it('takes a new owner from phone and code to shop setup and then home', async () => {
    renderAt('/login')

    await signIn(validCode)
    await userEvent.type(await screen.findByLabelText('Shop name'), 'Rahim Fashion')
    await userEvent.click(screen.getByRole('button', { name: 'Save and continue' }))

    expect(await screen.findByRole('heading', { name: 'Welcome to Hishab' })).toBeInTheDocument()
    expect(api.shopName).toBe('Rahim Fashion')
  })

  it('shows the server error under the code field', async () => {
    renderAt('/login')

    await signIn('000000')

    expect(await screen.findByRole('alert')).toHaveTextContent('The code is invalid or has expired')
    expect(screen.getByLabelText('6-digit code')).toHaveAttribute('aria-invalid', 'true')
  })

  it('validates the phone number before calling the API', async () => {
    renderAt('/login')

    await userEvent.type(await screen.findByLabelText('Mobile number'), '12345')
    await userEvent.click(screen.getByRole('button', { name: 'Send code' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Enter a Bangladeshi mobile number')
  })

  it('restores the session from the refresh cookie after a reload', async () => {
    api.hasRefreshCookie = true

    renderAt('/')

    expect(await screen.findByRole('heading', { name: 'Welcome to Hishab' })).toBeInTheDocument()
  })

  it('logs out back to the login page', async () => {
    api.hasRefreshCookie = true
    const router = renderAt('/')

    await userEvent.click(await screen.findByRole('button', { name: 'Log out' }))

    expect(await screen.findByLabelText('Mobile number')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
    expect(api.hasRefreshCookie).toBe(false)
  })

  it('signs a visitor in to the hosted demo with one click and marks every page as demo', async () => {
    api.demo = true
    renderAt('/login')

    await userEvent.click(await screen.findByRole('button', { name: 'Sign in as Owner' }))

    expect(await screen.findByRole('heading', { name: 'Welcome to Hishab' })).toBeInTheDocument()
    expect(screen.getByRole('note')).toHaveTextContent('Demo shop with sample data')
    expect(api.hasRefreshCookie).toBe(true)
  })

  it('lets a demo visitor try a staff role', async () => {
    api.demo = true
    renderAt('/login')

    await userEvent.click(await screen.findByRole('button', { name: 'Packer' }))

    expect(await screen.findByRole('heading', { name: 'Welcome to Hishab' })).toBeInTheDocument()
    expect(api.role).toBe('Packer')
  })

  it('shows no demo sign-in or banner outside the hosted demo', async () => {
    api.hasRefreshCookie = true
    renderAt('/')

    expect(await screen.findByRole('heading', { name: 'Welcome to Hishab' })).toBeInTheDocument()
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Try the demo' })).not.toBeInTheDocument()
  })

  it('switches the login page to Bangla', async () => {
    renderAt('/login')

    await userEvent.click(await screen.findByRole('button', { name: 'বাংলা' }))

    expect(screen.getByRole('heading', { name: 'হিসাব' })).toBeInTheDocument()
    expect(screen.getByLabelText('মোবাইল নম্বর')).toBeInTheDocument()
  })
})
