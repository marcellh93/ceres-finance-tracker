using System.Globalization;
using System.Reflection;
using FluentAssertions;
using ProjectCeres.Common;
using ProjectCeres.Controllers.Api;

namespace ProjectCeres.Tests.Unit;

/// <summary>
/// API keys, headers and filenames are machine-readable, so they must not change with the server's
/// culture. th-TH uses the Buddhist calendar (2026 is written 2569) and sv-SE writes a minus sign as
/// U+2212, which makes both visible; each test first proves the culture bites on this runtime.
/// </summary>
public class InvariantFormatTests
{
    private static readonly DateOnly Sep2026 = new(2026, 9, 1);

    private static T Under<T>(string culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Month_is_the_gregorian_year_even_under_a_buddhist_calendar_culture()
    {
        Under("th-TH", () => Sep2026.ToString("yyyy-MM")).Should().Be("2569-09", "otherwise this test proves nothing");

        Under("th-TH", () => InvariantFormat.Month(Sep2026)).Should().Be("2026-09");
    }

    [Fact]
    public void Day_is_the_gregorian_date_even_under_a_buddhist_calendar_culture()
    {
        Under("th-TH", () => Sep2026.ToString("yyyy-MM-dd")).Should().Be("2569-09-01");

        Under("th-TH", () => InvariantFormat.Day(Sep2026)).Should().Be("2026-09-01");
        Under("th-TH", () => InvariantFormat.Day(new DateTime(2026, 9, 1))).Should().Be("2026-09-01");
    }

    [Fact]
    public void Number_uses_an_ascii_minus_even_under_a_culture_that_writes_another_one()
    {
        Under("sv-SE", () => (-5).ToString()).Should().Be("−5", "otherwise this test proves nothing");

        Under("sv-SE", () => InvariantFormat.Number(-5)).Should().Be("-5");
    }

    [Fact]
    public void Export_filename_uses_gregorian_dates_even_under_a_buddhist_calendar_culture()
    {
        var build = typeof(MovementsApiController).GetMethod("BuildExportFileName", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Name(DateOnly? from, DateOnly? to) =>
            (string)build.Invoke(null, ["Checking", "transaction", from, to, "EUR"])!;
        var thru = new DateOnly(2026, 9, 30);

        Under("th-TH", () => Name(Sep2026, thru)).Should().Be("movements_eur_transactions_checking_2026-09-01_2026-09-30.csv");
        Under("th-TH", () => Name(Sep2026, null)).Should().Be("movements_eur_transactions_checking_from-2026-09-01.csv");
        Under("th-TH", () => Name(null, thru)).Should().Be("movements_eur_transactions_checking_to-2026-09-30.csv");
        Under("th-TH", () => Name(null, null)).Should().EndWith(
            $"_{DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv");
    }
}
