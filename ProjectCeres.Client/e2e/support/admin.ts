import { execFileSync } from 'node:child_process'

// The agent-reply leg of the support conversation needs an Admin user posting
// through POST /api/admin/support/tickets/{id}/messages ([RequireAdmin], a LIVE
// per-request DB role check — ADR-0080). There is no operator UI this stage, so
// the spec seeds the role directly, then drives the real HTTP endpoint.
//
// The app runs out-of-process (Playwright webServer), so we cannot reach its DI
// AdminRoleService. We grant the role in the E2E database with psql instead —
// the same tool tools/e2e/run-server.sh already uses to provision + wipe it.
// This is prerequisite setup (making an operator exist), not the feature under
// test, which is exactly the allowance in docs/testing.md § Rules.

// Connection to project_ceres_e2e as ceres_migrator (the owner — can write the
// Identity role tables). Mirrors run-server.sh's MIGRATOR_URI verbatim.
const E2E_DB_URI =
  process.env.E2E_MIGRATOR_URI ??
  'postgresql://ceres_migrator:ceres_migrator_dev_password@localhost/project_ceres_e2e'

function psql(sql: string): string {
  return execFileSync('psql', [E2E_DB_URI, '-tAqc', sql], {
    encoding: 'utf8',
  }).trim()
}

/**
 * Grants the Admin role to the user with the given email, in the E2E database.
 * Ensures the Admin role row exists first (E2E wipes auth data each run, and the
 * role is otherwise created lazily on the first in-app grant). Idempotent.
 *
 * Returns the granted user's id (also the ticket owner's id for later assertions).
 */
export function grantAdminByEmail(email: string): string {
  // This project normalizes identity emails to LOWERCASE, not Identity's default
  // uppercase (see BackfillIdentityNormalizedToLowercaseTests). Match that, or the
  // lookup never finds the just-registered user.
  const normalized = email.toLowerCase().replace(/'/g, "''")

  // 1. Ensure the "Admin" role row exists. NormalizedName is LOWERCASE here —
  //    this app's ILookupNormalizer lowercases role names (same convention as
  //    emails), so RoleManager.FindByNameAsync("Admin") looks up 'admin'. An
  //    uppercase 'ADMIN' row would never be found and the live check would 403.
  psql(`
    INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
    SELECT gen_random_uuid(), 'Admin', 'admin', gen_random_uuid()::text
    WHERE NOT EXISTS (SELECT 1 FROM "AspNetRoles" WHERE "NormalizedName" = 'admin');
  `)

  // 2. Resolve the user id from the normalized email.
  const userId = psql(
    `SELECT "Id" FROM "AspNetUsers" WHERE "NormalizedEmail" = '${normalized}';`,
  )
  if (!userId) throw new Error(`grantAdminByEmail: no user with email ${email}`)

  // 3. Pair the user with the Admin role (PK is (UserId, RoleId) — ON CONFLICT
  //    DO NOTHING keeps it idempotent).
  psql(`
    INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
    SELECT '${userId}', r."Id" FROM "AspNetRoles" r WHERE r."NormalizedName" = 'admin'
    ON CONFLICT DO NOTHING;
  `)

  return userId
}
