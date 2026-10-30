namespace CipherVault.Infrastructure.Import;

/// <summary>
/// Detects the source format of a CSV file based on its header line.
/// Supports Chrome, Edge, Firefox, and KeePass CSV formats.
/// </summary>
public static class BrowserFormatDetector
{
    /// <summary>
    /// Detects the format of a CSV file based on its first line (header).
    /// </summary>
    /// <param name="headerLine">The first line of the CSV file.</param>
    /// <returns>
    /// The detected format: "Chrome", "Firefox", "KeePass", or "Unknown".
    /// Note: Chrome and Edge have identical CSV headers, so Edge exports are detected as "Chrome".
    /// </returns>
    public static string DetectFormat(string headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
            return "Unknown";

        var headers = headerLine.ToLowerInvariant();

        // Firefox: has httpRealm or formActionOrigin columns
        if (headers.Contains("httprealm") || headers.Contains("formactionorigin"))
            return "Firefox";

        // KeePass: has Group, Title, Username columns (case-insensitive check)
        if (headers.Contains("group") && headers.Contains("title") && headers.Contains("username"))
            return "KeePass";

        // Chrome / Edge: name, url, username, password
        if (headers.Contains("name") && headers.Contains("url")
            && headers.Contains("username") && headers.Contains("password"))
            return "Chrome";

        return "Unknown";
    }
}
