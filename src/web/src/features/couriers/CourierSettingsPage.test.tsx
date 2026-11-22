import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import { vi } from 'vitest'
import App from '@/App'
import { routes } from '@/app/routes'
import { api, connected, courierList } from '@/test/server'

function openSettings() {
  api.hasRefreshCookie = true
  render(<App router={createMemoryRouter(routes, { initialEntries: ['/settings/couriers'] })} />)
}

const user = () => userEvent.setup()
const card = async (name: string) => within(await screen.findByRole('article', { name }))

describe('CourierSettingsPage', () => {
  it('connects a courier and never shows the typed key again', async () => {
    openSettings()
    const fake = await card('Test courier')
    const key = fake.getByLabelText('API key')
    expect(key).toHaveAttribute('type', 'password')
    expect(key).toHaveValue('')

    await user().type(key, 'fk_live_1234567890')
    await user().click(fake.getByRole('button', { name: 'Save and connect' }))

    expect(await fake.findByText(/Connected · key ••••7890/)).toBeInTheDocument()
    expect(fake.getByRole('status')).toHaveTextContent('will not be shown again')
    expect(api.courierPuts).toEqual([
      { provider: 'Fake', credentials: { apiKey: 'fk_live_1234567890' } },
    ])
    expect(screen.queryByDisplayValue('fk_live_1234567890')).not.toBeInTheDocument()
    expect(document.body.textContent).not.toContain('fk_live_1234567890')
  })

  it('builds the form from the courier fields and checks each one before sending', async () => {
    openSettings()
    const pathao = await card('Pathao')

    await user().click(pathao.getByRole('button', { name: 'Save and connect' }))

    expect(pathao.getAllByText('Enter this value.')).toHaveLength(2)
    expect(pathao.getByLabelText('Client ID')).toHaveAttribute('aria-invalid', 'true')
    expect(api.courierPuts).toEqual([])
  })

  it('shows the courier rejection and stays disconnected', async () => {
    openSettings()
    const fake = await card('Test courier')

    await user().type(fake.getByLabelText('API key'), 'invalid')
    await user().click(fake.getByRole('button', { name: 'Save and connect' }))

    expect(await fake.findByRole('alert')).toHaveTextContent('The courier rejected these keys.')
    expect(fake.getByText('Not connected')).toBeInTheDocument()
  })

  it('reveals the typed key only when asked', async () => {
    openSettings()
    const fake = await card('Test courier')

    await user().click(fake.getByLabelText('Show what I type'))

    expect(fake.getByLabelText('API key')).toHaveAttribute('type', 'text')
  })

  it('replaces keys through an empty form that can be cancelled', async () => {
    api.couriers = [connected('Fake')]
    openSettings()
    const fake = await card('Test courier')
    expect(fake.queryByLabelText('API key')).not.toBeInTheDocument()

    await user().click(fake.getByRole('button', { name: 'Replace keys' }))
    expect(fake.getByLabelText('API key')).toHaveValue('')
    await user().click(fake.getByRole('button', { name: 'Cancel' }))

    expect(fake.queryByLabelText('API key')).not.toBeInTheDocument()
  })

  it('disconnects only after the user confirms', async () => {
    api.couriers = [connected('Fake')]
    const ask = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true)
    openSettings()
    const fake = await card('Test courier')

    await user().click(fake.getByRole('button', { name: 'Disconnect' }))
    expect(fake.getByText(/Connected · key/)).toBeInTheDocument()
    await user().click(fake.getByRole('button', { name: 'Disconnect' }))

    expect(await fake.findByText('Not connected')).toBeInTheDocument()
    expect(ask).toHaveBeenCalledTimes(2)
  })

  it('makes a connected courier the default for bookings', async () => {
    api.couriers = [connected('Fake'), ...courierList().slice(1)]
    openSettings()
    const fake = await card('Test courier')

    await user().click(fake.getByRole('button', { name: 'Use as default' }))

    expect(await fake.findByText('Default')).toBeInTheDocument()
    expect(api.defaultCourier).toBe('Fake')
    expect(api.shopName).toBe('My shop') // the rest of the profile is sent back unchanged
  })

  it('tells packers and moderators that only owners and managers manage couriers', async () => {
    api.role = 'Packer'
    openSettings()

    expect(
      await screen.findByText('Only the owner or a manager can change courier connections.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('article')).not.toBeInTheDocument()
  })
})
