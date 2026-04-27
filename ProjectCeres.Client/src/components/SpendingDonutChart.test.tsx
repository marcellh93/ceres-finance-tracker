import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { SpendingDonutChart } from './SpendingDonutChart'

describe('SpendingDonutChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { categoryName: 'Groceries', amount: 420 },
        { categoryName: 'Housing', amount: 850 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<SpendingDonutChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<SpendingDonutChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
