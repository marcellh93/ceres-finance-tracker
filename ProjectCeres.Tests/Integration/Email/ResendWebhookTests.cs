using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using Xunit;

namespace ProjectCeres.Tests.Integration.Email;

[Collection("IntegrationTests")]
public sealed class ResendWebhookTests
{
    private readonly AuthTestWebApplicationFactory _factory;
    // Svix signing secrets are "whsec_" + base64(32-byte key). This is a deterministic
    // base64-encoded test key — DO NOT introduce non-base64 characters here; both the
    // verifier and this test's helper base64-decode the suffix.
    private const string TestWebhookSecret = "whsec_MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    public ResendWebhookTests(AuthTestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Rejects_bad_signature_with_401()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        var body = """{"type":"email.delivered","data":{"email_id":"00000000-0000-0000-0000-000000000000","to":["x@example.invalid"]}}""";
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", "msg_test_1");
        req.Headers.Add("Svix-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        req.Headers.Add("Svix-Signature", "v1,WRONG_SIGNATURE_BASE64==");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Accepts_valid_signature_and_records_event()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        // GUID-suffixed email so re-runs against the shared test database don't collide.
        var email = $"delivered-test-{Guid.NewGuid():N}@example.invalid";
        var body = "{\"type\":\"email.delivered\",\"data\":{\"email_id\":\"11111111-1111-1111-1111-111111111111\",\"to\":[\"" + email + "\"]}}";
        var signed = SignSvix(body, TestWebhookSecret, out var id, out var ts);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", id);
        req.Headers.Add("Svix-Timestamp", ts);
        req.Headers.Add("Svix-Signature", $"v1,{signed}");

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rec = await db.Set<EmailDeliveryEvent>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.EmailAddress == email);
        rec.Should().NotBeNull();
        rec!.Type.Should().Be("email.delivered");
    }

    [Fact]
    public async Task Bounced_event_flips_EmailConfirmed_to_false()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        // GUID-suffixed email so re-runs against the shared test database don't collide.
        var email = $"bounce-test-{Guid.NewGuid():N}@example.invalid";
        // Stage 9.1.5.b §4.6: project uses LowercaseLookupNormalizer in place of
        // Identity's default UpperInvariantLookupNormalizer, so NormalizedEmail is
        // stored in lowercase.
        var normalized = email.ToLowerInvariant();

        // Seed a user with EmailConfirmed = true
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            var create = await users.CreateAsync(user, "Pa$$w0rd!Test-7K");
            create.Succeeded.Should().BeTrue(
                "user seed must succeed; errors: " + string.Join(", ", create.Errors.Select(e => e.Description)));
        }

        var body = "{\"type\":\"email.bounced\",\"data\":{\"email_id\":\"22222222-2222-2222-2222-222222222222\",\"to\":[\"" + email + "\"]}}";
        var signed = SignSvix(body, TestWebhookSecret, out var id, out var ts);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", id);
        req.Headers.Add("Svix-Timestamp", ts);
        req.Headers.Add("Svix-Signature", $"v1,{signed}");

        var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.IgnoreQueryFilters()
                .FirstAsync(u => u.NormalizedEmail == normalized);
            user.EmailConfirmed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Timestamp_more_than_5_minutes_old_returns_401()
    {
        await using var factory = _factory.WithWebhookSecret(TestWebhookSecret);
        var client = factory.CreateClient();

        var body = """{"type":"email.delivered","data":{"email_id":"33333333-3333-3333-3333-333333333333","to":["x@example.invalid"]}}""";
        var oldTs = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var id = "msg_test_old";
        var signed = ComputeSvixHmac(id, oldTs, body, TestWebhookSecret);

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/internal/email-webhook/resend")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Svix-Id", id);
        req.Headers.Add("Svix-Timestamp", oldTs);
        req.Headers.Add("Svix-Signature", $"v1,{signed}");

        var resp = await client.SendAsync(req);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string SignSvix(string body, string secret, out string id, out string ts)
    {
        id = "msg_test_" + Guid.NewGuid().ToString("N");
        ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return ComputeSvixHmac(id, ts, body, secret);
    }

    private static string ComputeSvixHmac(string id, string ts, string body, string secret)
    {
        // Svix signing secret format: "whsec_<base64>". Strip prefix, decode base64.
        var keyB64 = secret.StartsWith("whsec_") ? secret["whsec_".Length..] : secret;
        var key = Convert.FromBase64String(keyB64.PadRight((keyB64.Length + 3) / 4 * 4, '='));
        using var hmac = new HMACSHA256(key);
        var toSign = Encoding.UTF8.GetBytes($"{id}.{ts}.{body}");
        return Convert.ToBase64String(hmac.ComputeHash(toSign));
    }
}
