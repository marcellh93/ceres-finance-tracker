using FluentAssertions;
using ProjectCeres.Services;

namespace ProjectCeres.Tests.Unit;

public class IdentifierRedactorTests
{
    [Fact]
    public void Redacts_the_users_own_email()
    {
        // Arrange
        var body = "You can reach me at alice@example.com for details.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "alice@example.com",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("You can reach me at [redacted] for details.");
    }

    [Fact]
    public void Redacts_the_users_display_name()
    {
        // Arrange
        var body = "This is Alice Smith speaking. I have an issue.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "Alice Smith",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("This is [redacted] speaking. I have an issue.");
    }

    [Fact]
    public void Pass2_catches_a_different_email()
    {
        // Arrange: body has a second email NOT in known → pass 2 regex should catch it
        var body = "The error happened when I imported data from bob@somewhere.org.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "alice@example.com",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("The error happened when I imported data from [redacted].");
    }

    [Fact]
    public void Pass2_catches_an_iban()
    {
        // Arrange
        var body = "My IBAN is ES91 2100 0418 4502 0005 1332 for the transfer.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("My IBAN is [redacted] for the transfer.");
    }

    [Fact]
    public void Leaves_ordinary_problem_text_untouched()
    {
        // Arrange
        var body = "The CSV import failed on semicolons. Can you help?";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be(body);
    }

    [Fact]
    public void Empty_known_fields_do_not_corrupt_body()
    {
        // Arrange: empty/whitespace fields should not cause empty-string replacements
        var body = "This is a normal problem description.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "   ",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be(body);
    }

    [Fact]
    public void Redacts_account_tokens()
    {
        // Arrange
        var body = "Account ACC123 has transactions. Token ACC456 is also here.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "",
            AccountTokens: new List<string> { "ACC123" }
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("Account [redacted] has transactions. Token ACC456 is also here.");
    }

    [Fact]
    public void Email_redaction_is_case_insensitive()
    {
        // Arrange: email should be case-insensitive per spec
        var body = "Contact us at ALICE@EXAMPLE.COM or alice@example.com";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "alice@example.com",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("Contact us at [redacted] or [redacted]");
    }

    [Fact]
    public void DisplayName_redaction_is_case_insensitive()
    {
        // Arrange: display name should be case-insensitive per spec
        var body = "Bob Smith and bob smith both appear here.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "Bob Smith",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("[redacted] and [redacted] both appear here.");
    }

    [Fact]
    public void Pass2_catches_unspaced_iban()
    {
        // Arrange: IBAN without spaces (common paste format)
        var body = "Transfer to account ES9121000418450200051332 please.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("Transfer to account [redacted] please.");
    }

    [Fact]
    public void Pass2_catches_lettered_iban()
    {
        // Arrange: GB IBAN with alphabetic bank code
        var body = "Use IBAN GB29NWBK60161331926819 for UK account.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("Use IBAN [redacted] for UK account.");
    }

    [Fact]
    public void Pass2_catches_lettered_iban_spaced()
    {
        // Arrange: GB IBAN with spaces (formatted)
        var body = "Account number: GB29 NWBK 6016 1331 9268 19.";
        var known = new IdentifierRedactor.ErasureIdentifiers(
            Email: "",
            DisplayName: "",
            AccountTokens: new List<string>()
        );

        // Act
        var result = IdentifierRedactor.Redact(body, known);

        // Assert
        result.Should().Be("Account number: [redacted].");
    }
}
