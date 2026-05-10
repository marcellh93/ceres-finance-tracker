using System.Globalization;
using Microsoft.AspNetCore.Authorization;

namespace ProjectCeres.Common.Authentication;

public sealed class RecentAuthRequirementHandler : AuthorizationHandler<RecentAuthRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RecentAuthRequirement requirement)
    {
        var raw = context.User.FindFirst(SessionConstants.LastReauthAtClaim)?.Value;
        if (string.IsNullOrEmpty(raw))
            return Task.CompletedTask;

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var claimUnix))
            return Task.CompletedTask;

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var ageSeconds = nowUnix - claimUnix;

        // Reject future claims (negative age). Boundary inclusive — a claim age == window passes.
        if (ageSeconds < 0) return Task.CompletedTask;
        if (ageSeconds <= (long)RecentAuthRequirement.Window.TotalSeconds)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
