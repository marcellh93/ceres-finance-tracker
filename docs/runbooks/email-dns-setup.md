# Email DNS Setup Runbook — Resend + SPF/DKIM/DMARC

> **When to execute:** as part of Stage 16 (hosting + ops), once a sending domain
> for Ceres has been registered and the Resend production account has been linked
> to that domain. Until then, sending happens from `onboarding@resend.dev` (Resend's
> shared sandbox sender) and SPF/DKIM/DMARC verification is not applicable. No
> domain is registered yet at the time this runbook is written; the records below
> are not published in any zone.
>
> **Pre-requisites:** registered domain (e.g. `ceres.example`), DNS zone with
> ability to add `TXT` and `CNAME` records, Resend account with admin access, the
> domain added to Resend → Domains.
>
> **Owner:** project maintainer (this is a one-time setup; the DKIM rotation
> procedure below is recurring).

## Step 1 — Verify the domain in Resend

1. Resend dashboard → Domains → Add Domain → enter `ceres.example`.
2. Resend displays three records to publish:
   - One **CNAME** for DKIM signing (selector subdomain).
   - One **TXT** for SPF.
   - (Optional, recommended) One **TXT** for the tracking subdomain.
3. Publish each record on the domain's DNS zone exactly as Resend specifies.
4. Click "Verify" in Resend. Wait for a green checkmark on all three records.

## Step 2 — Publish DMARC at `p=none`

Add a TXT record at `_dmarc.ceres.example`:

```
v=DMARC1; p=none; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

Set up `dmarc@ceres.example` as a real mailbox (or alias). DMARC aggregate
reports land here once a day from major mailbox providers.

## Step 3 — Verify with external tools

```bash
dig TXT ceres.example +short                             # should show v=spf1 include:_spf.resend.com -all
dig CNAME <selector>._domainkey.ceres.example +short     # should show the Resend DKIM CNAME
dig TXT _dmarc.ceres.example +short                      # should show v=DMARC1 p=none ...
```

Also run https://mxtoolbox.com/dmarc.aspx and https://mxtoolbox.com/dkim.aspx
for an external check.

## Step 4 — Advance DMARC policy

After 30 days of aggregate reports showing no legitimate Resend-signed mail
failing DMARC:

```
v=DMARC1; p=quarantine; pct=25; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

After another 30 days clean:

```
v=DMARC1; p=quarantine; pct=100; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

After another 30 days clean:

```
v=DMARC1; p=reject; rua=mailto:dmarc@ceres.example; aspf=s; adkim=s
```

Per `security-model.md` § Layer 1, the total ramp is ~90 days.

## Step 5 — MTA-STS + TLS-RPT (optional, hardening)

Publish a policy file at `https://mta-sts.ceres.example/.well-known/mta-sts.txt`:

```
version: STSv1
mode: enforce
mx: feedback-smtp.eu-west-1.amazonses.com
mx: *.resend.com
max_age: 86400
```

(Adjust the `mx:` lines to Resend's documented MX endpoints at the time you
publish.)

Add `_mta-sts.ceres.example` TXT:

```
v=STSv1; id=20260101T000000Z
```

Add `_smtp._tls.ceres.example` TXT:

```
v=TLSRPTv1; rua=mailto:tlsrpt@ceres.example
```

## Step 6 — Configure the production sender

Once the domain is verified in Resend, override the `From:` address on the
production deployment:

```
export Email__Resend__FromAddress="noreply@ceres.example"
export Email__Resend__FromName="Ceres"
```

Redeploy. Verify by triggering a password-reset request to your own email.
The `From:` header should now show `Ceres <noreply@ceres.example>`.

## Recurring — DKIM rotation (≥ every 6 months)

Resend supports dual-selector rotation. Per `security-model.md` § Layer 1
(DKIM key rotation):

1. Resend dashboard → Domains → Rotate DKIM. A new selector CNAME is shown.
2. Publish the new selector CNAME alongside the existing one.
3. In Resend, switch active signing to the new selector.
4. Wait 48 hours for DNS TTL.
5. Remove the old selector CNAME from DNS.
6. Update this runbook with the rotation date.

## Rollback

If DMARC `p=reject` causes legitimate mail to be rejected (third-party tools
sending on behalf of the domain that were not accounted for during the ramp),
revert to `p=quarantine; pct=25` immediately. Investigate via aggregate reports
before re-advancing.
