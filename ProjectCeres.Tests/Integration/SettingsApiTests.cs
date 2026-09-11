using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration;

[Collection("IntegrationParallel3")]
public class SettingsApiTests(Bucket3Factory factory, Bucket3Database bucketDb)
    : IntegrationTestBase<Bucket3Factory>(factory, bucketDb)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Get_Returns200_WithExpectedShape()
    {
        var response = await _client.GetAsync("/api/settings");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.ValueKind.Should().Be(JsonValueKind.Object);

        body.TryGetProperty("numberFormat", out var numberFormat).Should().BeTrue();
        numberFormat.GetString().Should().BeOneOf("comma_decimal", "period_decimal");

        body.TryGetProperty("dateFormat", out var dateFormat).Should().BeTrue();
        dateFormat.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("defaultCurrencyCode", out var code).Should().BeTrue();
        code.GetString().Should().NotBeNullOrEmpty();

        body.TryGetProperty("defaultCurrencySymbol", out var symbol).Should().BeTrue();
        symbol.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Patch_updates_settings_and_persists()
    {
        // Snapshot the current state so we can restore.
        var before = await _client.GetFromJsonAsync<JsonElement>("/api/settings");
        var beforeNumberFormat = before.GetProperty("numberFormat").GetString()!;
        var beforeDateFormat   = before.GetProperty("dateFormat").GetString()!;
        var beforePeriodStart  = before.GetProperty("periodStartDay").GetInt32();
        var beforeCurrencyCode = before.GetProperty("defaultCurrencyCode").GetString()!;

        try
        {
            var res = await _client.PatchAsJsonAsync("/api/settings", new
            {
                numberFormat      = "period_decimal",
                dateFormat        = "YYYY-MM-DD",
                defaultCurrencyId = 2,
                periodStartDay    = 15
            });
            res.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var after = await _client.GetFromJsonAsync<JsonElement>("/api/settings");
            after.GetProperty("numberFormat").GetString().Should().Be("period_decimal");
            after.GetProperty("dateFormat").GetString().Should().Be("YYYY-MM-DD");
            after.GetProperty("periodStartDay").GetInt32().Should().Be(15);
            after.GetProperty("defaultCurrencyCode").GetString().Should().Be("USD");
        }
        finally
        {
            // Restore. CurrencyId mapping: EUR=1, USD=2, GBP=3, COP=4, ARS=5, VED=6.
            var currencyId = beforeCurrencyCode switch
            {
                "EUR" => 1, "USD" => 2, "GBP" => 3, "COP" => 4, "ARS" => 5, "VED" => 6, _ => 1
            };
            await _client.PatchAsJsonAsync("/api/settings", new
            {
                numberFormat      = beforeNumberFormat,
                dateFormat        = beforeDateFormat,
                defaultCurrencyId = currencyId,
                periodStartDay    = beforePeriodStart
            });
        }
    }

    [Fact]
    public async Task Patch_returns_422_on_invalid_format()
    {
        var res = await _client.PatchAsJsonAsync("/api/settings", new
        {
            numberFormat      = "spanish_decimal",
            dateFormat        = "YYYY-MM-DD",
            defaultCurrencyId = 1,
            periodStartDay    = 1
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Patch_returns_422_on_unknown_currency()
    {
        var res = await _client.PatchAsJsonAsync("/api/settings", new
        {
            numberFormat      = "comma_decimal",
            dateFormat        = "DD/MM/YYYY",
            defaultCurrencyId = 999,
            periodStartDay    = 1
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("INVALID_CURRENCY");
    }

    [Fact]
    public async Task Patch_returns_422_on_period_start_day_out_of_range()
    {
        var res = await _client.PatchAsJsonAsync("/api/settings", new
        {
            numberFormat      = "comma_decimal",
            dateFormat        = "DD/MM/YYYY",
            defaultCurrencyId = 1,
            periodStartDay    = 32
        });
        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
