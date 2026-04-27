import { render, screen, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, afterEach } from 'vitest'
import { SpendingByCategoryChart, buildDisplayData } from './SpendingByCategoryChart'

describe('SpendingByCategoryChart', () => {
  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders without error and shows chart container', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => Promise.resolve([
        { categoryName: 'Groceries', amount: 420 },
        { categoryName: 'Housing', amount: 850 },
      ])
    }))
    render(<SpendingByCategoryChart />)
    await waitFor(() => {
      expect(document.querySelector('.recharts-wrapper')).toBeTruthy()
    })
  })

  it('shows loading state initially', () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      json: () => new Promise(() => {})
    }))
    render(<SpendingByCategoryChart />)
    expect(screen.getByText(/loading/i)).toBeTruthy()
  })

  describe('buildDisplayData', () => {
    it('returns all items when 5 or fewer', () => {
      const input = [
        { categoryName: 'A', amount: 100 },
        { categoryName: 'B', amount: 200 },
      ]
      const result = buildDisplayData(input)
      expect(result).toHaveLength(2)
      expect(result.map(d => d.categoryName)).toContain('A')
      expect(result.map(d => d.categoryName)).toContain('B')
    })

    it('groups categories beyond top 5 into Other', () => {
      const input = [
        { categoryName: 'A', amount: 500 },
        { categoryName: 'B', amount: 400 },
        { categoryName: 'C', amount: 300 },
        { categoryName: 'D', amount: 200 },
        { categoryName: 'E', amount: 100 },
        { categoryName: 'F', amount: 50 },
        { categoryName: 'G', amount: 30 },
      ]
      const result = buildDisplayData(input)
      expect(result).toHaveLength(6)
      const names = result.map(d => d.categoryName)
      expect(names).toContain('Other')
      expect(names).toContain('A')
      expect(names).not.toContain('F')
      expect(names).not.toContain('G')
      const other = result.find(d => d.categoryName === 'Other')!
      expect(other.amount).toBe(80)
    })

    it('sorts by amount descending', () => {
      const input = [
        { categoryName: 'Low', amount: 10 },
        { categoryName: 'High', amount: 999 },
        { categoryName: 'Mid', amount: 50 },
      ]
      const result = buildDisplayData(input)
      expect(result[0].categoryName).toBe('High')
    })
  })
})
