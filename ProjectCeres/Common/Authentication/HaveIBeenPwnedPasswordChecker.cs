using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace ProjectCeres.Common.Authentication;

public sealed class HaveIBeenPwnedPasswordChecker : IBreachedPasswordChecker
{
    private readonly HttpClient _http;

    public HaveIBeenPwnedPasswordChecker(HttpClient http) => _http = http;

    public async Task<bool> IsBreachedAsync(string password, CancellationToken ct = default)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(password));
        var hex = Convert.ToHexString(bytes);
        var prefix = hex[..5];
        var suffix = hex[5..];

        using var resp = await _http.GetAsync($"https://api.pwnedpasswords.com/range/{prefix}", ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync(ct);

        foreach (var line in body.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx < 35) continue;
            var lineSuffix = line[..idx].Trim();
            if (string.Equals(lineSuffix, suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
