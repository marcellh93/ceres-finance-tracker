namespace ProjectCeres.Common.Authentication;

/// <summary>
/// Masks an email address for display on an authenticated screen the owner may
/// leave unattended. Keeps the first character of the local part and of the domain;
/// everything else — including length and extension — is replaced by a fixed run of
/// dots, so the render discloses nothing beyond those two characters.
/// </summary>
public static class EmailMask
{
    // Fixed width: a 1-character local part and a 22-character one must render
    // identically, or the mask leaks length.
    private const string Dots = "•••••";

    /// <summary>Returns the masked form, or empty for absent or unparseable input.</summary>
    public static string Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "";

        var at = email.IndexOf('@', StringComparison.Ordinal);
        // Need a non-empty local part and domain either side of a single '@'.
        if (at <= 0 || at == email.Length - 1) return "";
        if (email.IndexOf('@', at + 1) >= 0) return "";

        var local = email[..at];
        var domain = email[(at + 1)..];

        // The extension is dropped rather than rendered: ".co.uk" narrows the domain
        // to a country, and localhost's *absence* of an extension would announce an
        // internal address. Omitting it everywhere removes both signals.
        return $"{local[0]}{Dots}@{domain[0]}{Dots}";
    }
}
