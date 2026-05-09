namespace ProjectCeres.Models;

public sealed class UserBlockedIp
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string IpAddress { get; set; } = "";
    public DateTime BlockedAt { get; set; }
    public string? Reason { get; set; }
}
