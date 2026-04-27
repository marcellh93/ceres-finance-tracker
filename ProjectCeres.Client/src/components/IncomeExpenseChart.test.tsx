import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { IncomeExpenseChart } from './IncomeExpenseChart'

describe('IncomeExpenseChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { month: '2025-11', income: 3500, expenses: 2100 },
        { month: '2025-12', income: 3200, expenses: 1900 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<IncomeExpenseChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<IncomeExpenseChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
