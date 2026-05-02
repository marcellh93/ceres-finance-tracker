namespace ProjectCeres.Common;

public interface ICurrentUserAccessor
{
    Guid UserId { get; }
}

/// <summary>
/// Phase 3 pre-auth implementation. Returns a single sentinel Guid that is stamped on
/// every user-owned row until real authentication lands. At auth time, this is replaced
/// by an HttpContext-backed implementation, and a one-shot data migration remaps the
/// sentinel to the first registered user's real Id (then the FK to AspNetUsers is added).
/// </summary>
public sealed class SingleUserAccessor : ICurrentUserAccessor
{
    public static readonly Guid SentinelUserId = new("00000000-0000-0000-0000-000000000001");
    public Guid UserId => SentinelUserId;
}
