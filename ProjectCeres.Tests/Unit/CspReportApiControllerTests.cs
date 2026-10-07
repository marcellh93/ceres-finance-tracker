using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ProjectCeres.Controllers.Api;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// The CSP report endpoint is anonymous, so whatever a caller sends ends up in our logs.
/// It must stay one bounded, single-line entry.
/// </summary>
public class CspReportApiControllerTests
{
    private static async Task<(IActionResult Result, List<string> Logs)> PostAsync(string body)
    {
        var logs = new List<string>();
        using var factory = LoggerFactory.Create(b => b.AddProvider(new InMemoryLoggerProvider(logs)));
        var controller = new CspReportApiController(factory.CreateLogger<CspReportApiController>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        var result = await controller.Report();
        return (result, logs);
    }

    [Fact]
    public async Task A_normal_report_is_logged_in_full_and_answered_with_204()
    {
        const string report = """{"csp-report":{"violated-directive":"script-src","blocked-uri":"https://evil.example/x.js"}}""";

        var (result, logs) = await PostAsync(report);

        result.Should().BeOfType<NoContentResult>();
        logs.Should().ContainSingle().Which.Should().Contain("script-src").And.Contain("https://evil.example/x.js");
    }

    [Fact]
    public async Task Line_breaks_and_control_characters_cannot_forge_extra_log_lines()
    {
        var (_, logs) = await PostAsync("{\"a\":1}\r\n[Production] Error: forged entry\n\u001b[31mred\u0000");

        var entry = logs.Should().ContainSingle().Subject;
        entry.Should().NotContainAny("\r", "\n", "\u001b", "\u0000");
        entry.Should().Contain("forged entry", "the text is kept, only the line breaks are neutralised");
    }

    [Fact]
    public async Task A_huge_report_is_truncated_and_says_so()
    {
        var (_, logs) = await PostAsync(new string('x', 200_000));

        var entry = logs.Should().ContainSingle().Subject;
        entry.Length.Should().BeLessThan(2_500, "the log entry is bounded however large the report is");
        entry.Should().Contain("truncated");
    }

    [Fact]
    public void The_endpoint_caps_the_request_body()
    {
        var attribute = typeof(CspReportApiController).GetMethod(nameof(CspReportApiController.Report))!
            .GetCustomAttributesData()
            .SingleOrDefault(a => a.AttributeType == typeof(RequestSizeLimitAttribute));

        attribute.Should().NotBeNull("an anonymous endpoint must not accept an unbounded body");
        ((long)attribute!.ConstructorArguments[0].Value!).Should().BeLessThanOrEqualTo(16 * 1024);
    }
}
