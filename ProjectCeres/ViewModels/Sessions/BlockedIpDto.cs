namespace ProjectCeres.ViewModels.Sessions;

public sealed record BlockedIpDto(
    string IpAddress,
    DateTime BlockedAt);
