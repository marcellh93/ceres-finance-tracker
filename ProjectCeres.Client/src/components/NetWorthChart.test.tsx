import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { NetWorthChart } from './NetWorthChart'

describe('NetWorthChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { month: '2025-11', assets: 12000, liabilities: 3000, netWorth: 9000 },
        { month: '2025-12', assets: 12500, liabilities: 2800, netWorth: 9700 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<NetWorthChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<NetWorthChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
