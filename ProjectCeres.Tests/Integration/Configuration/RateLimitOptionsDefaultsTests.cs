using FluentAssertions;
using Microsoft.Extensions.Configuration;
using ProjectCeres.Common.RateLimiting;
using Xunit;

namespace ProjectCeres.Tests.Integration.Configuration;

public class RateLimitOptionsDefaultsTests
{
    // With NO configuration overrides, the bound values must equal the literals that
    // shipped before Stage 9.11 — so the config binding can never silently weaken
    // production. Raised values live ONLY in appsettings.E2E.json.
    [Fact]
    public void Unconfigured_options_equal_the_pre_9_11_production_literals()
    {
        var config = new ConfigurationBuilder().Build(); // empty
        var opts = new RateLimitOptions();
        config.GetSection("RateLimits").Bind(opts);

        opts.LoginByIpPermitLimit.Should().Be(10);
        opts.CsrfByIpPermitLimit.Should().Be(60);
        opts.EmailByIpPermitLimit.Should().Be(10);
    }
}
