import { render, screen, waitFor } from '@testing-library/react'
import { GoalBudgetBars } from './GoalBudgetBars'

const mockData = [
  { id: '1', name: 'Trip to Japan', goalType: 'Spending', amountProgress: 1200, targetAmount: 3000, percentUsed: 40, currencyCode: 'EUR' },
  { id: '2', name: 'Emergency Fund', goalType: 'Savings', amountProgress: 2500, targetAmount: 5000, percentUsed: 50, currencyCode: 'EUR' },
]

beforeEach(() => {
  global.fetch = vi.fn().mockResolvedValue({
    ok: true,
    json: async () => mockData,
  } as Response)
})

test('renders a progress bar for each goal budget', async () => {
  render(<GoalBudgetBars />)

  await waitFor(() => {
    expect(screen.getByText('Trip to Japan')).toBeInTheDocument()
    expect(screen.getByText('Emergency Fund')).toBeInTheDocument()
  })
})

test('shows goal type labels', async () => {
  render(<GoalBudgetBars />)

  await waitFor(() => {
    expect(screen.getByText(/Spending/)).toBeInTheDocument()
    expect(screen.getByText(/Savings/)).toBeInTheDocument()
  })
})
