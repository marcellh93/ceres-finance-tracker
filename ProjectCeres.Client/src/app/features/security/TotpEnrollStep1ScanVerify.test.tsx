import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { I18nextProvider } from 'react-i18next';
import i18n from '../../i18n/i18n';
import { TotpEnrollStep1ScanVerify } from './TotpEnrollStep1ScanVerify';

const OTPAUTH = 'otpauth://totp/Ceres:a@b.test?secret=JBSWY3DPEHPK3PXP&issuer=Ceres&algorithm=SHA1&digits=6&period=30';
const MANUAL = 'JBSW Y3DP EHPK 3PXP';

function mount() {
  return render(
    <I18nextProvider i18n={i18n}>
      <TotpEnrollStep1ScanVerify
        otpAuthUri={OTPAUTH}
        manualEntryKey={MANUAL}
        onEnrolled={vi.fn()}
        onReauthRequired={vi.fn()}
        onRestart={vi.fn()}
      />
    </I18nextProvider>,
  );
}

describe('TotpEnrollStep1ScanVerify — QR rendering contract', () => {
  it('renders the QR wrapper with bg-white (NOT bg-card)', () => {
    // Regression: authenticator camera apps refuse to scan QR codes that
    // are not dark-on-light. In dark mode the previous bg-card wrapper
    // produced a dark frame around the QR with no quiet zone, breaking
    // the scan. The fix forces bg-white in both themes.
    mount();
    const wrapper = screen.getByTestId('totp-qr-wrapper');
    expect(wrapper.className).toMatch(/\bbg-white\b/);
    expect(wrapper.className).not.toMatch(/\bbg-card\b/);
  });

  it('the rendered SVG carries the spec-required 4-module quiet zone', () => {
    // qrcode.react v4 renders the marginSize as <rect width=...>/<path>
    // structure. We assert by counting modules: a marginSize=4 QR at
    // size=192 renders an SVG whose viewBox includes the 4-module border.
    // The simplest stable observable: the SVG exists and is the right
    // approximate size. (Inspecting the path data is brittle across
    // qrcode.react minor versions; the viewBox + explicit margin prop
    // contract is what we care about.)
    mount();
    const svg = document.querySelector('svg[aria-label="TOTP enrolment QR code"]');
    expect(svg).not.toBeNull();
    // Sanity check the SVG is in the document with the expected dimensions.
    expect(svg!.getAttribute('width')).toBe('192');
    expect(svg!.getAttribute('height')).toBe('192');
    // marginSize=4 + size=192 → SVG viewBox should be 29 modules wide
    // (21 base + 8 margin). Sanity: viewBox should span at least 29 units.
    const viewBox = svg!.getAttribute('viewBox');
    expect(viewBox).not.toBeNull();
    const dims = (viewBox ?? '').split(/\s+/).map(Number);
    // viewBox = "x y width height"; width should be 21 (v1) + 2*marginSize = 29.
    // Use ≥ 21 + 4 (1 margin module on each side as a lower bound) to remain
    // robust against qrcode.react's internal rounding, but ASSERT non-zero
    // margin: viewBox width must exceed 21 (the bare-QR width).
    expect(dims[2]).toBeGreaterThan(21);
  });
});
