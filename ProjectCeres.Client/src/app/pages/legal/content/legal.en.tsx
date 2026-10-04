/**
 * Legal Notice + Cookie Policy content (English) — DRAFT.
 *
 * Grounded in the actual codebase (cookie names + purposes) and
 * docs/legal.md. Business-identity facts (Aviso Legal) are unverifiable
 * from the code and are left as [CONFIRM WITH COUNSEL: ...] markers — never
 * invented. Do not publish as the live policy until counsel has reviewed it.
 */
export default function LegalContentEn() {
  return (
    <>
      <p>
        <strong>
          [DRAFT — generated from the codebase, NOT legally reviewed. Do not
          publish as the live policy until counsel has reviewed it.]
        </strong>
      </p>

      <p>Effective date: [CONFIRM: effective date].</p>

      <section id="aviso-legal">
        <h2>Legal notice (Aviso Legal)</h2>
        <p>
          In accordance with Article 10 of the LSSI-CE (Ley 34/2002, de
          Servicios de la Sociedad de la Información y de Comercio
          Electrónico), the following information identifies the party
          responsible for this website and service:
        </p>
        <ul>
          <li>Legal entity name: [CONFIRM: legal entity name]</li>
          <li>Tax identification number (NIF): [CONFIRM: NIF]</li>
          <li>Registered address: [CONFIRM: registered address]</li>
          <li>Contact email: [CONFIRM: contact email]</li>
        </ul>
      </section>

      <section id="cookies">
        <h2>Cookie policy</h2>
        <p>
          Project Ceres sets the following cookies. All of them are strictly
          necessary for the site to function and none of them are used for
          tracking or advertising; none of them are gated behind consent
          today, because none of them are optional.
        </p>
        <ul>
          <li>
            <strong>__Host-Session</strong> — maintains your signed-in
            session. Deleted when the session ends.
          </li>
          <li>
            <strong>__Host-Persist</strong> — keeps you signed in across
            visits when you choose "remember me."
          </li>
          <li>
            <strong>__Host-XSRF</strong> — protects state-changing requests
            against cross-site request forgery (CSRF).
          </li>
          <li>
            <strong>lang</strong> — remembers your preferred display
            language (English/Spanish).
          </li>
          <li>
            <strong>cookie_consent</strong> — records the choice you made in
            the cookie banner itself, so we don't ask again unnecessarily.
          </li>
        </ul>
        <p>
          Because every cookie above is strictly necessary, there are no
          optional or analytics cookies to opt into today. If that changes
          in the future, this policy and the consent banner will be updated
          before any new cookie is set.
        </p>
        <p>
          You can withdraw or change your cookie choice at any time with the
          same ease you gave it: use "Manage cookie preferences" in the
          footer of this site, or the equivalent option under Settings →
          Privacy & cookies if you are signed in, to reopen the consent
          banner.
        </p>
      </section>
    </>
  );
}
