using System.Text.RegularExpressions;

namespace ProjectCeres.Services;

/// <summary>
/// Pure function to redact user identifiers from a support message body.
/// Pass 1: targeted replacement of known identifiers (email, display name, account tokens).
/// Pass 2: generic regex patterns for email and IBAN shapes.
/// </summary>
public static class IdentifierRedactor
{
    /// <summary>
    /// Identifiers known to belong to the user being erased.
    /// </summary>
    public record ErasureIdentifiers(
        string Email,
        string DisplayName,
        IReadOnlyList<string> AccountTokens
    );

    /// <summary>
    /// Redacts user identifiers from a message body. Pure function, no I/O.
    /// </summary>
    /// <param name="body">The message body to redact.</param>
    /// <param name="known">The identifiers to redact.</param>
    /// <returns>The body with identifiers replaced by [redacted].</returns>
    public static string Redact(string body, ErasureIdentifiers known)
    {
        var result = body;

        // Pass 1: targeted replacement of known identifiers (case-insensitive for email and display name)
        if (!string.IsNullOrWhiteSpace(known.Email))
        {
            result = Replace(result, known.Email, ignoreCase: true);
        }

        if (!string.IsNullOrWhiteSpace(known.DisplayName))
        {
            result = Replace(result, known.DisplayName, ignoreCase: true);
        }

        foreach (var token in known.AccountTokens)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                result = Replace(result, token, ignoreCase: false);
            }
        }

        // Pass 2: generic regex patterns for email and IBAN shapes
        // Email pattern: word chars, dots, plus, hyphen @ domain
        result = Regex.Replace(
            result,
            @"\b[\w.+-]+@[\w-]+\.[\w.-]+\b",
            "[redacted]",
            RegexOptions.IgnoreCase
        );

        // IBAN pattern: country code (2 letters) + check digits (2) + account
        // Covers spaced (ES91 2100 0418...) and unspaced (ES9121000418...) formats, including lettered BBANs (GB29NWBK...)
        // Pattern 1: unspaced 11-30 alphanumerics; Pattern 2: space-separated groups ending with a digit group
        result = Regex.Replace(
            result,
            @"\b[A-Z]{2}\d{2}[A-Z0-9]{11,30}\b|\b[A-Z]{2}\d{2}\s[A-Z0-9]{1,4}(?:\s\d{1,4})+\b",
            "[redacted]",
            RegexOptions.IgnoreCase
        );

        return result;
    }

    /// <summary>
    /// Case-sensitive or case-insensitive string replacement.
    /// </summary>
    private static string Replace(string input, string oldValue, bool ignoreCase)
    {
        if (string.IsNullOrEmpty(oldValue))
            return input;

        if (!ignoreCase)
        {
            return input.Replace(oldValue, "[redacted]", StringComparison.Ordinal);
        }

        // Case-insensitive replacement
        var regex = new Regex(Regex.Escape(oldValue), RegexOptions.IgnoreCase);
        return regex.Replace(input, "[redacted]");
    }
}
