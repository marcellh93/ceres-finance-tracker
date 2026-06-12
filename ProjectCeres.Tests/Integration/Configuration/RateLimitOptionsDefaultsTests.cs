using System.IO;
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

    // The on-disk BASE appsettings.json loads in EVERY environment, including Production.
    // The raised permit counts must live ONLY in appsettings.E2E.json — if a raised value
    // ever leaked into the base file, Production would silently inherit it. Bind the real
    // base file and assert the values still equal the pre-9.11 defaults. (bind-and-check
    // approach, per the gap brief's preference.)
    [Fact]
    public void Base_appsettings_does_not_raise_rate_limits()
    {
        var baseAppsettings = ResolveBaseAppsettingsPath();

        var config = new ConfigurationBuilder()
            .AddJsonFile(baseAppsettings, optional: false)
            .Build();

        var opts = new RateLimitOptions();
        config.GetSection("RateLimits").Bind(opts);

        opts.LoginByIpPermitLimit.Should().Be(10,
            "base appsettings.json must not raise LoginByIpPermitLimit — raised values are E2E-only");
        opts.CsrfByIpPermitLimit.Should().Be(60,
            "base appsettings.json must not raise CsrfByIpPermitLimit — raised values are E2E-only");
        opts.EmailByIpPermitLimit.Should().Be(10,
            "base appsettings.json must not raise EmailByIpPermitLimit — raised values are E2E-only");
    }

    // Walks up from the test bin output (.../ProjectCeres.Tests/bin/Debug/net10.0/) to the
    // repo root (identified by ProjectCeres.sln), then descends into the production project.
    private static string ResolveBaseAppsettingsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ProjectCeres.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("repo root (ProjectCeres.sln) not found from test bin directory");

        var path = Path.Combine(dir.FullName, "ProjectCeres", "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Could not resolve base appsettings.json: {path}");
        return path;
    }
}
