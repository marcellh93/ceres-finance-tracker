namespace ProjectCeres.ViewModels.Sessions;

// IP in the body, not the URL: IPv6 addresses carry colons (and can be zone-suffixed),
// which are awkward to encode in a path segment and would leak the address into server
// access logs. Mirrors BlockIpRequest.
public sealed record UnblockIpRequest(string? IpAddress);
