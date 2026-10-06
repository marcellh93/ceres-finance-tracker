import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it } from 'vitest';
import { InfoTip } from './InfoTip';

const TIP = 'Unspent room left in each budget.';
const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

function setup() {
  render(
    <div>
      <InfoTip about="Budget reserved" tip={TIP} />
      <p>elsewhere</p>
    </div>,
  );
  return screen.getByRole('button', { name: 'About Budget reserved' });
}

describe('InfoTip', () => {
  it('opens when the icon is clicked (touch has no hover)', async () => {
    const button = setup();
    expect(screen.queryByText(TIP)).toBeNull();

    await userEvent.click(button);

    expect(await screen.findByText(TIP)).toBeDefined();
  });

  it('stays open when clicked after hovering past Base UI\'s 500ms patient-click window', async () => {
    const button = setup();

    await userEvent.hover(button);
    expect(await screen.findByText(TIP)).toBeDefined();
    await sleep(700);
    await userEvent.click(button);
    await sleep(100);

    expect(screen.queryByText(TIP)).not.toBeNull();
  });

  it('pins on a quick click right after the hover preview appears, surviving pointer leave', async () => {
    const button = setup();

    await userEvent.hover(button);
    await screen.findByText(TIP);
    await userEvent.click(button);
    await userEvent.unhover(button);
    await sleep(300);

    expect(screen.queryByText(TIP)).not.toBeNull();
  });

  it('stays open after a click even when the pointer leaves the icon', async () => {
    const button = setup();

    await userEvent.click(button);
    await screen.findByText(TIP);
    await userEvent.unhover(button);
    await sleep(300);

    expect(screen.queryByText(TIP)).not.toBeNull();
  });

  it('closes when the icon is clicked again after pinning', async () => {
    const button = setup();

    await userEvent.click(button);
    await screen.findByText(TIP);
    await sleep(700);
    await userEvent.click(button);

    await waitFor(() => expect(screen.queryByText(TIP)).toBeNull());
  });

  it('closes on Escape', async () => {
    const button = setup();

    await userEvent.click(button);
    await screen.findByText(TIP);
    await userEvent.keyboard('{Escape}');

    await waitFor(() => expect(screen.queryByText(TIP)).toBeNull());
  });

  it('closes when clicking elsewhere on the page', async () => {
    const button = setup();

    await userEvent.click(button);
    await screen.findByText(TIP);
    await userEvent.click(screen.getByText('elsewhere'));

    await waitFor(() => expect(screen.queryByText(TIP)).toBeNull());
  });

  it('a hover-only preview closes when the pointer leaves', async () => {
    const button = setup();

    await userEvent.hover(button);
    await screen.findByText(TIP);
    await userEvent.unhover(button);

    await waitFor(() => expect(screen.queryByText(TIP)).toBeNull());
  });
});
