import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { CashFlowChart } from './CashFlowChart'

describe('CashFlowChart', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { month: '2025-11', netFlow: 1400 },
        { month: '2025-12', netFlow: -200 },
      ])
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    render(<CashFlowChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    render(<CashFlowChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })
})
