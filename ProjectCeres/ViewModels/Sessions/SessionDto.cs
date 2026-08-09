namespace ProjectCeres.ViewModels.Sessions;

public sealed record SessionDto(
    Guid Id,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    string IpCreatedAt,
    string UserAgent,
    bool IsCurrent);
