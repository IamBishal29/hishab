import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter } from 'react-router'
import App from '@/App'
import { routes } from '@/app/routes'
import { chatDraft, emptyDraft } from '@/test/chatDraftApi'
import { api } from '@/test/server'

const chat = 'Apa 2 ta polo L dien. Rahima, House 4 Road 2, Mirpur 10. 01712345678'

const draft = emptyDraft({
  phone: '+8801712345678',
  customerName: 'Rahima',
  deliveryAddress: 'House 4 Road 2, Mirpur 10, Dhaka',
  isInsideDhaka: true,
  lines: [
    {
      variantId: 'v-l',
      description: 'polo L',
      quantity: 2,
      label: 'Polo shirt · L',
      unitPrice: 700,
    },
    { variantId: null, description: 'blue scarf', quantity: 1, label: null, unitPrice: null },
  ],
  warnings: ['unmatchedItem', 'severalPhones'],
})

const user = () => userEvent.setup()

async function openForm() {
  api.hasRefreshCookie = true
  render(<App router={createMemoryRouter(routes, { initialEntries: ['/orders/new'] })} />)
  await screen.findByLabelText('Customer mobile')
}

async function pasteAndFill(text = chat) {
  await user().click(screen.getByText('Paste a chat to fill the form'))
  await user().click(screen.getByLabelText('Conversation with the buyer'))
  await user().paste(text)
  await user().click(screen.getByRole('button', { name: 'Fill the form' }))
}

/** A field's label reads "<name> Suggested" while it holds the assistant's value. */
const field = (name: string) => screen.getByLabelText(new RegExp(`^${name}`))
const suggested = (name: string) =>
  screen.queryByLabelText(new RegExp(`^${name}\\s*✦\\s*Suggested$`))

describe('Paste a chat to draft an order', () => {
  it('fills the form, marks every suggested field and lists what needs a hand, without saving', async () => {
    chatDraft.next = draft
    await openForm()

    await pasteAndFill()

    const filled = await screen.findByText(/Check every field marked Suggested/)
    const outcome = filled.closest('[role="status"]')
    if (!(outcome instanceof HTMLElement)) throw new Error('The outcome is not a live region')
    expect(within(outcome).getByText('“blue scarf” × 1')).toBeInTheDocument()
    expect(within(outcome).getByText(/more than one number/)).toBeInTheDocument()
    expect(field('Customer mobile')).toHaveValue('01712345678')
    expect(suggested('Customer mobile')).not.toBeNull()
    expect(suggested('Customer name')).toHaveValue('Rahima')
    expect(suggested('Delivery address')).toHaveValue('House 4 Road 2, Mirpur 10, Dhaka')
    expect(screen.getByLabelText('Quantity of Polo shirt · L')).toHaveValue(2)
    expect(chatDraft.chats).toEqual([chat])
    expect(api.orderRequests).toHaveLength(0) // a draft is only a suggestion
  })

  it('never replaces what the seller typed', async () => {
    chatDraft.next = draft
    await openForm()
    await user().type(field('Customer name'), 'Rahima Begum')

    await pasteAndFill()

    expect(await screen.findByLabelText('Quantity of Polo shirt · L')).toHaveValue(2)
    expect(field('Customer name')).toHaveValue('Rahima Begum')
    expect(suggested('Customer name')).toBeNull()
    expect(suggested('Delivery address')).not.toBeNull()
  })

  it('drops the mark as soon as the seller edits a suggested field, and saves only on Save', async () => {
    chatDraft.next = emptyDraft({
      phone: '+8801712345678',
      customerName: 'Rahima',
      lines: [
        {
          variantId: 'v-l',
          description: 'polo',
          quantity: 1,
          label: 'Polo shirt · L',
          unitPrice: 700,
        },
      ],
    })
    await openForm()
    await pasteAndFill()
    await screen.findByLabelText('Quantity of Polo shirt · L')

    await user().type(field('Customer name'), ' Begum')
    expect(suggested('Customer name')).toBeNull()

    await user().click(screen.getByRole('button', { name: 'Save order' }))

    expect(await screen.findByText(/Order #\d+ saved/)).toBeInTheDocument()
    expect(api.orderRequests).toHaveLength(1)
    expect(api.orderRequests[0]?.body).toMatchObject({
      phone: '01712345678',
      customerName: 'Rahima Begum',
      lines: [{ variantId: 'v-l', quantity: 1 }],
    })
    // The next order starts clean: no marks, empty paste box.
    expect(screen.queryByText('Suggested')).toBeNull()
    await user().click(screen.getByText('Paste a chat to fill the form'))
    expect(screen.getByLabelText('Conversation with the buyer')).toHaveValue('')
  })

  it('says so when the assistant is unavailable and leaves the form as it was', async () => {
    chatDraft.next = 503
    await openForm()

    await pasteAndFill()

    expect(await screen.findByRole('alert')).toHaveTextContent(/enter the order by hand/)
    expect(field('Customer mobile')).toHaveValue('')
    expect(screen.getByRole('button', { name: 'Fill the form' })).toBeEnabled()
  })
})
