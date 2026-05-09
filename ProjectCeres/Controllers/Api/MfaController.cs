using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.ViewModels.Auth;

namespace ProjectCeres.Controllers.Api;

[ApiController]
[Route("api/auth/mfa")]
[Authorize]
public sealed class MfaController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public MfaController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll()
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key)) return StatusCode(500);

        var encodedIssuer = Uri.EscapeDataString(MfaConstants.Issuer);
        var encodedEmail = Uri.EscapeDataString(user.Email ?? "");
        var otpAuthUri = $"otpauth://totp/{encodedIssuer}:{encodedEmail}?secret={key}&issuer={encodedIssuer}&algorithm=SHA1&digits=6&period=30";
        var manualEntryKey = FormatManualKey(key);

        ApplyNoStoreHeaders();
        return Ok(new { otpAuthUri, manualEntryKey });
    }

    [HttpPost("enroll/verify")]
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
            return BadRequest(new { error = "no_enrollment_in_progress" });
        }

        var verified = await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, request.Code);
        if (!verified)
        {
            return BadRequest(new { error = "code_did_not_verify" });
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var codes = await backupCodes.GenerateAndPersistAsync(user.Id, HttpContext.RequestAborted);

        ApplyNoStoreHeaders();
        return Ok(new { backupCodes = codes });
    }

    [HttpPost("backup-codes/regenerate")]
    public async Task<IActionResult> RegenerateBackupCodes(
        [FromServices] MfaBackupCodeService backupCodes)
    {
        var user = await GetCurrentUserAsync();
        if (user is null) return Unauthorized();

        if (!user.TwoFactorEnabled)
        {
            return BadRequest(new { error = "mfa_not_enabled" });
        }

        var codes = await backupCodes.RegenerateAsync(user.Id, HttpContext.RequestAborted);

        ApplyNoStoreHeaders();
        return Ok(new { backupCodes = codes });
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

    private void ApplyNoStoreHeaders()
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
    }
}
