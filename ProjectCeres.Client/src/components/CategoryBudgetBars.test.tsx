import { render, screen, waitFor } from '@testing-library/react'
import { CategoryBudgetBars } from './CategoryBudgetBars'

const mockData = [
  { id: '1', categoryName: 'Housing', currencyCode: 'EUR', spent: 400, limit: 500, percentUsed: 80 },
  { id: '2', categoryName: 'Groceries', currencyCode: 'EUR', spent: 120, limit: 300, percentUsed: 40 },
]

beforeEach(() => {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    json: async () => mockData,
  } as Response)
})

test('renders a progress bar for each category budget', async () => {
  render(<CategoryBudgetBars />)

  await waitFor(() => {
    expect(screen.getByText('Housing')).toBeInTheDocument()
    expect(screen.getByText('Groceries')).toBeInTheDocument()
  })
})

test('shows spent and limit amounts', async () => {
  render(<CategoryBudgetBars />)

  await waitFor(() => {
    expect(screen.getByText(/400/)).toBeInTheDocument()
    expect(screen.getByText(/500/)).toBeInTheDocument()
  })
})
