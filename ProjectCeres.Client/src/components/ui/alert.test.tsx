import { render, screen } from '@testing-library/react';
import { userEvent } from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Alert, AlertTitle, AlertDescription } from './alert';

describe('Alert', () => {
  it('announces via role=status and renders the message', () => {
    render(
      <Alert>
        <AlertDescription>Heads up</AlertDescription>
      </Alert>,
    );
    const alert = screen.getByRole('status');
    expect(alert).toHaveTextContent('Heads up');
    // The warning surround + AA body token are applied.
    expect(alert.className).toContain('bg-warning/10');
    expect(alert.className).toContain('text-warning-foreground');
  });

  it('renders the default warning icon, hidden from the a11y tree', () => {
    const { container } = render(
      <Alert>
        <AlertDescription>x</AlertDescription>
      </Alert>,
    );
    // lucide renders an <svg aria-hidden>; the icon stroke stays in text-warning (AA-Large).
    const svg = container.querySelector('svg[aria-hidden="true"]');
    expect(svg).not.toBeNull();
    expect(svg!.getAttribute('class')).toContain('text-warning');
  });

  it('suppresses the icon when icon={null}', () => {
    const { container } = render(
      <Alert icon={null}>
        <AlertDescription>no icon here</AlertDescription>
      </Alert>,
    );
    expect(container.querySelector('svg')).toBeNull();
  });

  it('renders a labelled 44px dismiss button only when onDismiss is given, and calls it', async () => {
    const onDismiss = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(
      <Alert onDismiss={onDismiss} dismissLabel="Dismiss">
        <AlertDescription>closable</AlertDescription>
      </Alert>,
    );
    const btn = screen.getByRole('button', { name: 'Dismiss' });
    // Touch-target minimum: size-11 = 44px.
    expect(btn.className).toContain('size-11');
    await user.click(btn);
    expect(onDismiss).toHaveBeenCalledOnce();

    rerender(
      <Alert>
        <AlertDescription>not closable</AlertDescription>
      </Alert>,
    );
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('AlertTitle is emphasised', () => {
    render(
      <Alert>
        <AlertTitle>Title</AlertTitle>
        <AlertDescription>Body</AlertDescription>
      </Alert>,
    );
    expect(screen.getByText('Title').className).toContain('font-medium');
  });
});
