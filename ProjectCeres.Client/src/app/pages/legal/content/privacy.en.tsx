/**
 * Privacy Policy content (English) — DRAFT.
 *
 * Grounded in the actual codebase (cookie names, data categories, retention
 * windows) and docs/legal.md. Every fact below is verifiable; every business
 * or legal judgment that isn't is wrapped as [CONFIRM WITH COUNSEL: ...] and
 * must never be filled with a guess. Do not publish as the live policy until
 * counsel has reviewed it.
 */
export default function PrivacyContentEn() {
  return (
    <>
      <p>
        <strong>
          [DRAFT — generated from the codebase, NOT legally reviewed. Do not
          publish as the live policy until counsel has reviewed it.]
        </strong>
      </p>

      <p>
        Effective date: [CONFIRM: effective date]. This policy explains what
        personal data Project Ceres collects, why, how long it is kept, who
        it is shared with, and the rights you have over it.
      </p>

      <h2>What data we collect</h2>
      <p>We collect the following categories of personal data:</p>
      <ul>
        <li>Your account email address.</li>
        <li>
          Your IP address and user-agent string, recorded in security logs:
          failed sign-in attempts, active session records, the audit log,
          and blocked-IP records.
        </li>
        <li>
          The file names of attachments you upload, and the attachment files
          themselves (e.g. receipts, invoices).
        </li>
        <li>
          Your financial data: accounts, transactions, budgets, and
          categories.
        </li>
      </ul>

      <h2>Why we process it (legal basis)</h2>
      <p>
        We rely on the following legal bases for processing, under GDPR
        Article 6:
      </p>
      <ul>
        <li>
          [CONFIRM WITH COUNSEL: legal basis — service delivery (account,
          transactions, budgets, categories) = contract]
        </li>
        <li>
          [CONFIRM WITH COUNSEL: legal basis — financial-record retention =
          legal obligation]
        </li>
        <li>
          [CONFIRM WITH COUNSEL: legal basis — security logs (failed
          sign-ins, sessions, audit log, blocked IPs) = legitimate interest]
        </li>
      </ul>

      <h2>How long we keep it</h2>
      <p>We keep data for the following periods:</p>
      <ul>
        <li>Soft-deleted saved reports: 90 days, then automatically purged.</li>
        <li>Audit logs: 12 months, then automatically purged.</li>
        <li>Failed sign-in logs: 1 year, then automatically purged.</li>
        <li>
          Account closure without an erasure request (natural churn): a
          30-day grace period with full reactivation, followed by a 150-day
          sealed archive, followed by permanent deletion at day 180.
        </li>
        <li>
          Account closure with a GDPR erasure request: personal identifiers
          are anonymised immediately after a 72-hour cancel-only hold (an
          emailed link lets you cancel the request within that window; after
          72 hours the hold cannot be reversed).
        </li>
      </ul>
      <p>
        <strong>Financial records are retained for longer than the above,
        and this overrides the right to erasure.</strong> Spanish law (Código
        de Comercio, Art. 30) requires accounting records to be kept for 6
        years, and tax-relevant records (Ley General Tributaria) for 4 to 6
        years. GDPR Article 17(3)(b) permits this: it exempts legally
        required retention from the right to erasure. In practice, if you
        request erasure, your personal identifiers (name, email, and other
        direct identifiers) are anonymised, but the underlying anonymised
        financial records — accounts, transactions, budgets, categories —
        are retained for the legal retention period described above before
        deletion.
      </p>

      <h2>Who we share it with</h2>
      <p>
        We do not use third-party behavioural analytics or advertising
        trackers in the current phase of the product. Data is shared only
        with the service providers needed to operate the app:
      </p>
      <ul>
        <li>
          [CONFIRM: hosting provider — name + confirmation a Data Processing
          Agreement (DPA) has been signed]
        </li>
        <li>
          [CONFIRM: email service provider — name + confirmation a Data
          Processing Agreement (DPA) has been signed]
        </li>
      </ul>

      <h2>Your rights</h2>
      <p>
        Under GDPR you have the right to access, rectify, erase, port, and
        object to the processing of your personal data. In the app:
      </p>
      <ul>
        <li>
          <strong>Access and portability:</strong> go to Settings → Account
          and use the option to download a copy of your data.
        </li>
        <li>
          <strong>Rectification:</strong> update your information directly
          through the normal account and data-entry screens.
        </li>
        <li>
          <strong>Erasure:</strong> go to Settings → Account and request
          account closure with erasure. You will receive an email with a
          cancel link valid for 72 hours; after that window, anonymisation
          proceeds as described above.
        </li>
        <li>
          <strong>Objection and restriction:</strong> contact us using the
          details below.
        </li>
      </ul>

      <h2>How to contact us</h2>
      <p>
        To exercise any of the rights above, or for any question about this
        policy: [CONFIRM WITH COUNSEL: contact email address]. Our Data
        Protection Officer and the supervisory authority for complaints:
        [CONFIRM WITH COUNSEL: DPO contact details and supervisory-authority
        (AEPD) contact details].
      </p>
    </>
  );
}
