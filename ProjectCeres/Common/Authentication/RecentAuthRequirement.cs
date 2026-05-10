using Microsoft.AspNetCore.Authorization;

namespace ProjectCeres.Common.Authentication;

public sealed class RecentAuthRequirement : IAuthorizationRequirement
{
    /// <summary>5-minute freshness window per security-model.md § Login → Reauthentication.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
}
