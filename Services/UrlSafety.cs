namespace ServantSync.Services;

/// <summary>
/// Security helpers for validating URLs that originate from
/// user-controlled input (query strings, form fields) before
/// navigating to them. The primary caller is the auth flow: an
/// attacker who can craft <c>?returnUrl=https://evil.com</c> can
/// otherwise bounce a signed-in user to a phishing page after
/// sign-in or sign-up.
/// </summary>
public static class UrlSafety
{
    /// <summary>
    /// Returns true only for same-origin, path-only relative URLs.
    /// Rejects:
    ///   * empty / null inputs,
    ///   * absolute URLs ("https://evil.com", "http://evil.com"),
    ///   * protocol-relative URLs ("//evil.com"),
    ///   * backslash-escaped variants ("/\evil.com" — some
    ///     browsers normalize these to forward slashes).
    /// Does NOT validate that the path is a known route in this
    /// app — that's a routing concern, not a security one. A
    /// relative path that doesn't match any route will simply
    /// 404, which is the desired fail-closed behavior.
    ///
    /// URL-encoding note: ASP.NET Core's model binder URL-decodes
    /// query string values exactly once before binding them to
    /// the [SupplyParameterFromQuery] property, so a value like
    /// <c>?returnUrl=/%2F%2Fevil.com</c> arrives here as the
    /// decoded string "//evil.com" and is correctly caught by
    /// the literal-character checks. Do NOT "fix" this by adding
    /// a separate <c>!url.StartsWith("/%2F", ...)</c> check —
    /// it's both redundant (the decoding already happened) and
    /// would let future percent-encoded variants through if the
    /// check list ever falls out of sync with the encoder.
    /// </summary>
    public static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url.StartsWith('/')
        && !url.StartsWith("//")
        && !url.StartsWith("/\\");
}
