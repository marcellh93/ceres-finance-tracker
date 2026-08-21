import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest'
import { ClearedBadge } from './ClearedBadge'
import { installCsrfFetchMock, resetCsrfCache, TEST_CSRF_TOKEN } from '../test/csrf-fetch-mock'

describe('ClearedBadge', () => {
  const id = '11111111-1111-1111-1111-111111111111'
  const type = 'transaction'

  beforeEach(async () => {
    // apiFetch runs a one-time CSRF handshake before the first state-changing
    // request; this mock serves it so queued responses still line up.
    await resetCsrfCache()
    installCsrfFetchMock()
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

  it('renders Needs review badge when isCleared is false and needsReview is true', () => {
    render(<ClearedBadge id={id} type={type} isCleared={false} needsReview={true} />)
    expect(screen.getByText('Needs review')).toBeTruthy()
  })

  it('renders Cleared badge (not Needs review) when isCleared is true and needsReview is true', () => {
    render(<ClearedBadge id={id} type={type} isCleared={true} needsReview={true} />)
    expect(screen.getByText('Cleared')).toBeTruthy()
  })

  // -------------------------------------------------------------------------
  // Click calls PATCH with correct body
  // -------------------------------------------------------------------------

  it('calls PATCH /api/movements/{id}/cleared with correct body on click', async () => {
    const mockFetch = installCsrfFetchMock()
    mockFetch.mockResolvedValue({ ok: true, status: 204, headers: { get: () => null } })

    render(<ClearedBadge id={id} type={type} isCleared={false} />)
    fireEvent.click(screen.getByRole('button'))

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith(
        `/api/movements/${id}/cleared`,
        expect.objectContaining({
          method: 'PATCH',
          // The CSRF header is the fix: without it antiforgery rejects with 400.
          headers: expect.objectContaining({
            'Content-Type': 'application/json',
            'X-XSRF-TOKEN': TEST_CSRF_TOKEN,
          }),
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
    installCsrfFetchMock().mockImplementation(() => pendingFetch)

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
    installCsrfFetchMock().mockResolvedValue({ ok: false, status: 400, headers: { get: () => null }, json: async () => null })

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
    installCsrfFetchMock().mockImplementation(() => Promise.reject(new Error('network error')))

    render(<ClearedBadge id={id} type={type} isCleared={false} />)
    fireEvent.click(screen.getByRole('button'))

    expect(screen.getByText('Cleared')).toBeTruthy()

    await waitFor(() => expect(screen.getByText('Pending')).toBeTruthy())
  })
})
