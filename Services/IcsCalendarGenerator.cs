using System.Text;

namespace ServantSync.Services;

/// <summary>
/// A single VEVENT for <see cref="IcsCalendarGenerator"/>. The generator
/// formats timestamps with the <c>Z</c> suffix. <see cref="DateTimeKind"/>
/// is interpreted as: <c>Utc</c> is passed through; <c>Unspecified</c>
/// is treated as UTC (the value the seeder / assigner produce);
/// <c>Local</c> is converted to UTC via
/// <see cref="DateTime.ToUniversalTime"/>.
/// </summary>
public class IcsEvent
{
    public string Uid { get; set; } = "";
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Summary { get; set; } = "";
    public string? Description { get; set; }
    public string? Location { get; set; }

    /// <summary>Defaults to <c>now</c> passed to the generator.</summary>
    public DateTime? StampUtc { get; set; }
}

/// <summary>
/// Pure RFC-5545 subset generator for text/calendar feeds. Handles
/// property escaping (commas, semicolons, backslashes, newlines) and
/// formats all timestamps as UTC <c>YYYYMMDDTHHMMSSZ</c>. Content
/// lines are folded at 75 octets per RFC 5545 §3.1 with
/// <c>\r\n</c> + single-space continuation so strictly-conformant
/// clients (some Outlook builds) accept the feed.
/// </summary>
public static class IcsCalendarGenerator
{
    /// <summary>
    /// RFC 5545 §3.1 — Content lines SHOULD be limited to 75 octets,
    /// excluding the line break. Long content lines MUST be folded by
    /// inserting a CRLF followed by a single linear white-space
    /// character. Any subsequent line begins with that single
    /// white-space character as the "continuation indicator".
    /// </summary>
    private const int MaxLineOctets = 75;

    public static string Generate(string productId, IEnumerable<IcsEvent> events, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        AppendFolded(sb, $"PRODID:-//{Escape(productId)}//EN");
        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        sb.Append("METHOD:PUBLISH\r\n");
        foreach (var e in events)
        {
            sb.Append("BEGIN:VEVENT\r\n");
            AppendFolded(sb, $"UID:{Escape(e.Uid)}");
            AppendFolded(sb, $"DTSTAMP:{FormatUtc(e.StampUtc ?? nowUtc)}");
            AppendFolded(sb, $"DTSTART:{FormatUtc(e.StartUtc)}");
            AppendFolded(sb, $"DTEND:{FormatUtc(e.EndUtc)}");
            AppendFolded(sb, $"SUMMARY:{Escape(e.Summary)}");
            if (!string.IsNullOrEmpty(e.Description))
                AppendFolded(sb, $"DESCRIPTION:{Escape(e.Description)}");
            if (!string.IsNullOrEmpty(e.Location))
                AppendFolded(sb, $"LOCATION:{Escape(e.Location)}");
            sb.Append("END:VEVENT\r\n");
        }
        sb.Append("END:VCALENDAR\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Appends <paramref name="line"/> to <paramref name="sb"/>, folding
    /// at <see cref="MaxLineOctets"/> octets per RFC 5545 §3.1. The
    /// continuation marker is a single SPACE; the next line begins
    /// with that SPACE so conforming parsers can re-join the segments
    /// on whitespace elision. The fold is octet-based (UTF-8 encoded)
    /// because RFC 5545 measures content lines in octets, not chars.
    /// </summary>
    private static void AppendFolded(StringBuilder sb, string line)
    {
        // Encode once to measure in octets. Re-encoded per chunk for the
        // same reason — the chunk boundaries need to be OCTET boundaries,
        // not char boundaries, and char->octet length depends on which
        // characters land in the chunk (multi-byte UTF-8 sequences).
        var allBytes = Encoding.UTF8.GetBytes(line);
        if (allBytes.Length <= MaxLineOctets)
        {
            sb.Append(line);
            sb.Append("\r\n");
            return;
        }

        // Walk char-by-char and keep the running octet count under
        // MaxLineOctets. Emit a CRLF + SPACE at every fold boundary.
        var runningBytes = 0;
        var started = false;
        var charIdx = 0;
        while (charIdx < line.Length)
        {
            if (started && runningBytes >= MaxLineOctets)
            {
                sb.Append("\r\n ");
                runningBytes = 1; // the SPACE continuation indicator
                started = false;
            }
            // Tentatively append the next char using a small lookahead
            // window — UTF-8 encoding of a single char is 1-4 octets
            // and the boundary may need to skip past a multi-byte char
            // so we don't split it across the fold.
            var ch = line[charIdx];
            var charBytes = Encoding.UTF8.GetBytes(new[] { ch });
            if (runningBytes + charBytes.Length > MaxLineOctets && runningBytes > 0)
            {
                sb.Append("\r\n ");
                runningBytes = 1;
                started = false;
            }
            sb.Append(ch);
            runningBytes += charBytes.Length;
            started = true;
            charIdx++;
        }
        sb.Append("\r\n");
    }

    private static string FormatUtc(DateTime utc)
    {
        // Contract: StartUtc / EndUtc are already UTC. .NET's ToUniversalTime()
        // treats DateTimeKind.Unspecified as local and shifts the value, which
        // would corrupt the output. Honor the contract by coercing
        // Unspecified to UTC explicitly. Local is still converted (in case a
        // caller passes a Local DateTime they know is in their local zone).
        utc = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc),
        };
        return utc.ToString("yyyyMMddTHHmmssZ");
    }

    private static string Escape(string text) =>
        text
            .Replace("\\", "\\\\")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\r\n", "\\n")
            .Replace("\n", "\\n")
            .Replace("\r", "\\n");
}
