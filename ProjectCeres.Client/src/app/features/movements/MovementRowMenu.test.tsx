import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MovementRowMenu } from './MovementRowMenu';

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const mockFetch = vi.fn();

beforeEach(() => {
  global.fetch = mockFetch as unknown as typeof fetch;
});

afterEach(() => {
  vi.resetAllMocks();
});

function renderMenu({
  movementId = 'tx1',
  movementType = 'Transaction' as const,
  onDeleted = vi.fn(),
} = {}) {
  render(
    <MemoryRouter initialEntries={['/movements']}>
      <Routes>
        <Route
          path="/movements"
          element={
            <MovementRowMenu
              movementId={movementId}
              movementType={movementType}
              onDeleted={onDeleted}
            />
          }
        />
        <Route
          path="/movements/:id/edit"
          element={<div data-testid="edit-page">Edit Page</div>}
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe('MovementRowMenu', () => {
  it('trigger button is in the document and keyboard-reachable (has role=button and aria-label)', () => {
    renderMenu();
    const trigger = screen.getByRole('button', { name: /row actions/i });
    expect(trigger).toBeInTheDocument();
    // keyboard reachable: not disabled, not aria-hidden
    expect(trigger).not.toBeDisabled();
    expect(trigger).not.toHaveAttribute('aria-hidden', 'true');
  });

  it('opens the menu and shows Edit and Delete items when trigger is clicked', async () => {
    renderMenu();
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    await waitFor(() => {
      expect(screen.getByText('Edit')).toBeInTheDocument();
      expect(screen.getByText('Delete')).toBeInTheDocument();
    });
  });

  it('navigates to /movements/:id/edit when Edit is clicked', async () => {
    renderMenu({ movementId: 'tx1' });
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    const editItem = await screen.findByText('Edit');
    fireEvent.click(editItem);
    await waitFor(() => {
      expect(screen.getByTestId('edit-page')).toBeInTheDocument();
    });
  });

  it('opens AlertDialog when Delete item is clicked', async () => {
    renderMenu();
    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    const deleteItem = await screen.findByText('Delete');
    fireEvent.click(deleteItem);
    await waitFor(() => {
      expect(screen.getByText('Delete this movement?')).toBeInTheDocument();
    });
  });

  it('calls correct DELETE endpoint and onDeleted on confirm (Transaction)', async () => {
    const onDeleted = vi.fn();
    mockFetch.mockResolvedValue({ status: 204 });
    renderMenu({ movementId: 'tx1', movementType: 'Transaction', onDeleted });

    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    const deleteItem = await screen.findByText('Delete');
    fireEvent.click(deleteItem);

    // Wait for dialog to open, then click confirm
    await screen.findByText('Delete this movement?');
    const confirmButtons = screen.getAllByRole('button', { name: /delete/i });
    // last one is the dialog confirm button
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith('/api/transactions/tx1', { method: 'DELETE' });
      expect(onDeleted).toHaveBeenCalledTimes(1);
    });
  });

  it('calls correct DELETE endpoint for Transfer', async () => {
    const onDeleted = vi.fn();
    mockFetch.mockResolvedValue({ status: 204 });
    renderMenu({ movementId: 'tr1', movementType: 'Transfer', onDeleted });

    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    const deleteItem = await screen.findByText('Delete');
    fireEvent.click(deleteItem);

    await screen.findByText('Delete this movement?');
    const confirmButtons = screen.getAllByRole('button', { name: /delete/i });
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith('/api/transfers/tr1', { method: 'DELETE' });
      expect(onDeleted).toHaveBeenCalledTimes(1);
    });
  });

  it('calls correct DELETE endpoint for LiabilityPayment', async () => {
    const onDeleted = vi.fn();
    mockFetch.mockResolvedValue({ status: 204 });
    renderMenu({ movementId: 'lp1', movementType: 'LiabilityPayment', onDeleted });

    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    const deleteItem = await screen.findByText('Delete');
    fireEvent.click(deleteItem);

    await screen.findByText('Delete this movement?');
    const confirmButtons = screen.getAllByRole('button', { name: /delete/i });
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() => {
      expect(mockFetch).toHaveBeenCalledWith('/api/liability-payments/lp1', { method: 'DELETE' });
      expect(onDeleted).toHaveBeenCalledTimes(1);
    });
  });

  it('does not call DELETE and onDeleted when Cancel is clicked', async () => {
    const onDeleted = vi.fn();
    renderMenu({ onDeleted });

    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    const deleteItem = await screen.findByText('Delete');
    fireEvent.click(deleteItem);

    await screen.findByText('Delete this movement?');
    const cancelButton = screen.getByRole('button', { name: /cancel/i });
    fireEvent.click(cancelButton);

    await waitFor(() => {
      expect(mockFetch).not.toHaveBeenCalled();
      expect(onDeleted).not.toHaveBeenCalled();
    });
  });

  it('shows toast.error and does not call onDeleted on 422 response', async () => {
    const { toast } = await import('sonner');
    const onDeleted = vi.fn();
    mockFetch.mockResolvedValue({
      status: 422,
      json: async () => ({
        error: {
          code: 'VALIDATION_ERROR',
          message: 'Cannot delete a movement linked to an active budget.',
          details: [],
        },
      }),
    });
    renderMenu({ onDeleted });

    fireEvent.click(screen.getByRole('button', { name: /row actions/i }));
    const deleteItem = await screen.findByText('Delete');
    fireEvent.click(deleteItem);

    await screen.findByText('Delete this movement?');
    const confirmButtons = screen.getAllByRole('button', { name: /delete/i });
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'Cannot delete a movement linked to an active budget.',
      );
      expect(onDeleted).not.toHaveBeenCalled();
    });
  });
});
