using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using ProjectCeres.Common;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/mfa")]
[Authorize]
public sealed class MfaController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogWriter _auditLog;
    private readonly IEmailComposer _composer;
    private readonly IEmailService _email;
    private readonly IEmailRecipientResolver _recipients;
    private readonly ILanguageResolver _languages;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MfaController> _logger;

    public MfaController(
        UserManager<ApplicationUser> userManager,
        IAuditLogWriter auditLog,
        IEmailComposer composer,
        IEmailService email,
        IEmailRecipientResolver recipients,
        ILanguageResolver languages,
        TimeProvider timeProvider,
        ILogger<MfaController> logger)
    {
        _userManager = userManager;
        _auditLog = auditLog;
        _composer = composer;
        _email = email;
        _recipients = recipients;
        _languages = languages;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpPost("enroll")]
    [RequireRecentAuth]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
    public async Task<IActionResult> Enroll()
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();

        if (user.TwoFactorEnabled)
            return Conflict(new { error = new { code = "MFA_ALREADY_ENROLLED", message = "MFA is already enabled. Disable MFA first to re-enroll." } });

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key)) return StatusCode(500);

        var encodedIssuer = Uri.EscapeDataString(MfaConstants.Issuer);
        var encodedEmail = Uri.EscapeDataString(user.Email ?? "");
        var otpAuthUri = $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={key}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
        var manualEntryKey = FormatManualKey(key);

        Response.ApplyNoStore();
        return Ok(new { otpAuthUri, manualEntryKey });
    }

    [HttpPost("enroll/verify")]
    [RequireRecentAuth]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
    public async Task<IActionResult> EnrollVerify(
        [FromBody] EnrollVerifyRequest request,
        [FromServices] MfaBackupCodeService backupCodes)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();

        var hasKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(hasKey))
        {
            return BadRequest(new { error = new { code = "NO_ENROLLMENT_IN_PROGRESS", message = "Call POST /api/auth/mfa/enroll first to start enrollment." } });
        }

        var verified = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
        if (!verified)
        {
            return BadRequest(new { error = new { code = "INVALID_MFA_CODE", message = "The verification code is invalid or expired." } });
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await backupCodes.GenerateAndPersistAsync(user.Id, HttpContext.RequestAborted);

        await _auditLog.RecordAsync(user.Id, AuditLogAction.MfaEnrolled, ct: HttpContext.RequestAborted);
        await SendSecurityEventEmailAsync(user.Id, EmailTemplateKey.TotpEnrolled);

        Response.ApplyNoStore();
        return Ok(new { backupCodes = codes });
    }

    [HttpPost("backup-codes/regenerate")]
    [RequireRecentAuth]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
    public async Task<IActionResult> RegenerateBackupCodes(
        [FromServices] MfaBackupCodeService backupCodes)
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();

        if (!user.TwoFactorEnabled)
            return Conflict(new { error = new { code = "MFA_NOT_ENABLED", message = "MFA must be enabled to regenerate backup codes." } });

        var codes = await backupCodes.RegenerateAsync(user.Id, HttpContext.RequestAborted);

        await _auditLog.RecordAsync(user.Id, AuditLogAction.BackupCodesRegenerated, ct: HttpContext.RequestAborted);
        await SendSecurityEventEmailAsync(user.Id, EmailTemplateKey.BackupCodesRegenerated);

        Response.ApplyNoStore();
        return Ok(new { backupCodes = codes });
    }

    /// <summary>
    /// Stage 9.6 — Turns off two-factor sign-in for the calling user. Flips
    /// <c>TwoFactorEnabled = false</c>, resets the authenticator key so a re-enrolment
    /// starts clean, purges persisted backup codes so old codes can't be used after
    /// re-enrolment, and writes an <c>MfaDisabled</c> audit-log row.
    /// </summary>
    [HttpPost("disable")]
    [RequireRecentAuth]
    [EnableRateLimiting(AuthRateLimitPolicies.AuthMfaByUser)]
    public async Task<IActionResult> Disable(
        [FromServices] MfaBackupCodeService backupCodes)
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();

        if (!user.TwoFactorEnabled)
            return Conflict(new { error = new { code = "MFA_NOT_ENABLED", message = "MFA is not enabled." } });

        await _userManager.SetTwoFactorEnabledAsync(user, false);
        await _userManager.ResetAuthenticatorKeyAsync(user);
        await backupCodes.PurgeAsync(user.Id, HttpContext.RequestAborted);

        await _auditLog.RecordAsync(user.Id, AuditLogAction.MfaDisabled, ct: HttpContext.RequestAborted);
        await SendSecurityEventEmailAsync(user.Id, EmailTemplateKey.TotpDisabled);

        Response.ApplyNoStore();
        return NoContent();
    }

    // Fire-and-log security-event email. Failures never fail the MFA operation,
    // which already committed before this runs.
    private async Task SendSecurityEventEmailAsync(Guid userId, EmailTemplateKey key)
    {
        try
        {
            var recipient = await _recipients.ResolveAsync(userId, HttpContext.RequestAborted);
            var culture = await _languages.ResolveForUserAsync(userId, HttpContext.RequestAborted);
            var timestamp = _timeProvider.GetUtcNow().UtcDateTime
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
            var msg = _composer.Compose(key, culture, timestamp, ip) with { To = recipient };
            await _email.SendAsync(msg, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send {Key} security-event email; the MFA operation already completed.", key);
        }
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var sid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return sid is null ? null : await _userManager.FindByIdAsync(sid);
    }

    private static string FormatManualKey(string key)
    {
        var sb = new System.Text.StringBuilder(key.Length + key.Length / 4);
        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append(' ');
            sb.Append(key[i]);
        }
        return sb.ToString();
    }

}
