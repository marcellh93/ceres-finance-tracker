import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { AccountBalancesChart } from './AccountBalancesChart'

describe('AccountBalancesChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { accountName: 'Checking', balance: 4200 },
        { accountName: 'Savings', balance: 8500 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<AccountBalancesChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<AccountBalancesChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
