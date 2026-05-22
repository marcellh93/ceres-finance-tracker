using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ProjectCeres.Data;
using ProjectCeres.Models;
using Microsoft.AspNetCore.Identity;

namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Runs AFTER UseAuthentication. Gate: !context.User.Identity!.IsAuthenticated AND
/// __Host-Persist cookie present. Looks up matching UserSession by hashed token,
/// rotates (revoke old, issue new), signs the user in for the response cookie.
/// THIS request stays unauthenticated → 401; browser retries with the fresh
/// __Host-Session cookie. Preserves the SecurityStampValidator / SessionRevocationValidator
/// invariant for every authenticated hop (Stage 6b.3 Gap 11).
///
/// 2026-05-22 — previously gated on "session cookie absent"; that missed the case where
/// the browser keeps sending __Host-Session after its server-side ticket expired.
/// </summary>
public sealed class PersistentCookieRotationMiddleware
{
    private readonly RequestDelegate _next;

    // Per-session-row semaphore: serializes concurrent rotation attempts for the same
    // matched session row within this app process. Single-host only; multi-host fix
    // is in planning-phase3.md § Stage 6b.2 deferred decisions.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _rotationLocks = new();

    public PersistentCookieRotationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        AppDbContext db,
        PersistentTokenService tokens,
        SignInManager<ApplicationUser> signInManager)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }
        if (!context.Request.Cookies.TryGetValue(SessionConstants.PersistentCookieName, out var rawCookie)
            || string.IsNullOrWhiteSpace(rawCookie))
        {
            await _next(context);
            return;
        }

        var parsed = tokens.TryParseCookie(rawCookie);
        if (parsed is null)
        {
            // Malformed cookie (e.g., legacy raw-secret-only format from pre-6b.3).
            // Short-circuit before any Argon2id work.
            await _next(context);
            return;
        }

        var (sessionId, secret) = parsed.Value;
        // Cross-tenant by design: looks up session by cookie-embedded ID before UseAuthentication runs — no user in scope. Stage 10 architecture test allow-lists this file.
        var match = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.Id == sessionId && s.IsPersistent && s.RevokedAt == null && s.PersistentTokenHash != null)
            .FirstOrDefaultAsync();

        if (match is null || !tokens.Verify(secret, match.PersistentTokenHash!))
        {
            await _next(context);
            return;
        }

        // Serialize rotation per matched session row. The other parallel request that
        // matched the same row will block here, then re-check after acquiring and find
        // the row already revoked — short-circuiting to next() without re-rotating.
        var sem = _rotationLocks.GetOrAdd(match.Id, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            // Re-load to see post-rotation state from the parallel request, if any.
            await db.Entry(match).ReloadAsync();
            if (match.RevokedAt is not null)
            {
                // Another request already rotated this token. Don't double-rotate.
                await _next(context);
                return;
            }

            // Rotate. Issue new cookie in {base64url(sessionIdBytes)}.{secret} format (Gap 3).
            // Hash only the secret; the session ID is stored plaintext in the cookie and indexed in DB.
            match.RevokedAt = DateTime.UtcNow;

            var newSessionId = Guid.NewGuid();
            var newSecret = tokens.Generate();
            var newSession = new UserSession
            {
                Id = newSessionId,
                UserId = match.UserId,
                PersistentTokenHash = tokens.Hash(newSecret),
                IpCreatedAt = context.Connection.RemoteIpAddress?.ToString() ?? "",
                UserAgent = context.Request.Headers.UserAgent.ToString(),
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow,
                IsPersistent = true,
            };
            db.UserSessions.Add(newSession);
            await db.SaveChangesAsync();

            var newCookieValue = tokens.FormatCookie(newSessionId, newSecret);
            context.Response.Cookies.Append(
                SessionConstants.PersistentCookieName,
                newCookieValue,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = context.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddDays(30),
                });

            // Signal to the SPA that this 401 is the one-extra-round-trip cost
            // of the persistent-cookie rotation (Gap 11), NOT a genuine session
            // expiry. The client's silent-401 seam (useApi / apiFetch) checks
            // this header and skips its anon-transition dispatch when present —
            // otherwise the browser would never get to send the second request
            // with the freshly-issued session cookie before being redirected to
            // /login. The header is safe to expose: no secrets, no PII, just a
            // boolean flag readable by the SPA.
            context.Response.Headers[SessionConstants.CookieRotatedHeader] = "true";

            var user = await signInManager.UserManager.FindByIdAsync(match.UserId.ToString());
            if (user is null)
            {
                await _next(context);
                return;
            }

            // Sign into Identity (writes __Host-Session response cookie for next request).
            // SignInAsync also sets context.User as a framework side-effect; immediately
            // clear it so THIS request remains unauthenticated. SecurityStampValidator
            // and SessionRevocationValidator must run on every authenticated hop — the
            // browser will retry with the freshly-issued cookie on the next request
            // (Stage 6b.3 Gap 11).
            context.Items[SessionConstants.PendingSessionItemKey] = newSessionId;
            await signInManager.SignInAsync(user, isPersistent: false);
            context.User = new System.Security.Claims.ClaimsPrincipal();
        }
        finally
        {
            sem.Release();
        }

        await _next(context);
    }
}
