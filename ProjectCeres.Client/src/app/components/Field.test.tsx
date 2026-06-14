import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { Field } from './Field';

describe('Field', () => {
  it('gives the error <p> an id derived from htmlFor when error is present', () => {
    render(
      <Field label="Email" htmlFor="email" error="Bad">
        <input id="email" />
      </Field>,
    );
    const errorEl = document.getElementById('email-error');
    expect(errorEl).not.toBeNull();
    expect(errorEl?.textContent).toBe('Bad');
  });

  it('does not render an #email-error element when no error is present', () => {
    render(
      <Field label="Email" htmlFor="email">
        <input id="email" />
      </Field>,
    );
    expect(document.getElementById('email-error')).toBeNull();
  });

  it('renders no id on the error <p> when htmlFor is omitted', () => {
    render(
      <Field label="Composite" error="Oops">
        <input />
      </Field>,
    );
    const errorEl = screen.getByText('Oops');
    expect(errorEl.getAttribute('id')).toBeNull();
  });
});
