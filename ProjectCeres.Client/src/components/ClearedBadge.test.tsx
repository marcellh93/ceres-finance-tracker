import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { ClearedBadge } from './ClearedBadge'

describe('ClearedBadge', () => {
  const id = '11111111-1111-1111-1111-111111111111'
  const type = 'transaction'

  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // -------------------------------------------------------------------------
  // Static rendering
  // -------------------------------------------------------------------------

  it('renders Cleared badge when isCleared is true', () => {
    render(<ClearedBadge id={id} type={type} isCleared={true} />)
    expect(screen.getByText('Cleared')).toBeTruthy()
  })

  it('renders Pending badge when isCleared is false', () => {
    render(<ClearedBadge id={id} type={type} isCleared={false} />)
    expect(screen.getByText('Pending')).toBeTruthy()
  })

  // -------------------------------------------------------------------------
  // Click calls PATCH with correct body
  // -------------------------------------------------------------------------

  it('calls PATCH /api/movements/{id}/cleared with correct body on click', async () => {
    const mockFetch = vi.fn().mockResolvedValue({ ok: true })
    vi.stubGlobal('fetch', mockFetch)

    render(<ClearedBadge id={id} type={type} isCleared={false} />)
    fireEvent.click(screen.getByRole('button'))

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        `/api/movements/${id}/cleared`,
        expect.objectContaining({
          method: 'PATCH',
          headers: expect.objectContaining({ 'Content-Type': 'application/json' }),
          body: JSON.stringify({ type, cleared: true }),
        })
      )
    })
  })

  // -------------------------------------------------------------------------
  // Optimistic toggle — flips immediately before response
  // -------------------------------------------------------------------------

  it('flips badge optimistically before response resolves', async () => {
    let resolvePromise!: () => void
    const pendingFetch = new Promise<{ ok: true }>((res) => {
      resolvePromise = () => res({ ok: true })
    })
    vi.stubGlobal('fetch', vi.fn().mockReturnValue(pendingFetch))

    render(<ClearedBadge id={id} type={type} isCleared={false} />)
    expect(screen.getByText('Pending')).toBeTruthy()

    fireEvent.click(screen.getByRole('button'))

    // optimistic — badge flips before the promise resolves
    expect(screen.getByText('Cleared')).toBeTruthy()

    resolvePromise()
    await waitFor(() => expect(screen.getByText('Cleared')).toBeTruthy())
  })

  // -------------------------------------------------------------------------
  // Reverts on error
  // -------------------------------------------------------------------------

  it('reverts badge to original state when API call fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: false }))

    render(<ClearedBadge id={id} type={type} isCleared={false} />)
    fireEvent.click(screen.getByRole('button'))

    // optimistic flip
    expect(screen.getByText('Cleared')).toBeTruthy()

    // after failed response, reverts
    await waitFor(() => expect(screen.getByText('Pending')).toBeTruthy())
  })

  // -------------------------------------------------------------------------
  // Reverts on network error
  // -------------------------------------------------------------------------

  it('reverts badge when fetch throws', async () => {
    vi.stubGlobal('fetch', vi.fn().mockRejectedValue(new Error('network error')))

    render(<ClearedBadge id={id} type={type} isCleared={false} />)
    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByText('Cleared')).toBeTruthy()

    await waitFor(() => expect(screen.getByText('Pending')).toBeTruthy())
  })
})
