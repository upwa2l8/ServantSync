using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

public class IcsCalendarGeneratorTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Generate_EmptyEventList_ReturnsCalendarShellOnly()
    {
        var result = IcsCalendarGenerator.Generate("ServantSync", Array.Empty<IcsEvent>(), Now);
        Assert.Contains("BEGIN:VCALENDAR", result);
        Assert.Contains("END:VCALENDAR", result);
        Assert.Contains("PRODID:-//ServantSync//EN", result);
        Assert.Contains("VERSION:2.0", result);
        Assert.DoesNotContain("BEGIN:VEVENT", result);
    }

    [Fact]
    public void Generate_OneEventWithAllProperties_ProducesAllFields()
    {
        var ev = new IcsEvent
        {
            Uid = "assignment-1@servantsync.local",
            StartUtc = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Utc),
            Summary = "Sunday Sound Tech",
            Description = "Run the board",
            Location = "Sound booth",
        };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);

        Assert.Contains("BEGIN:VEVENT", result);
        Assert.Contains("UID:assignment-1@servantsync.local", result);
        Assert.Contains("DTSTAMP:20260101T000000Z", result);
        Assert.Contains("DTSTART:20260709T140000Z", result);
        Assert.Contains("DTEND:20260709T160000Z", result);
        Assert.Contains("SUMMARY:Sunday Sound Tech", result);
        Assert.Contains("DESCRIPTION:Run the board", result);
        Assert.Contains("LOCATION:Sound booth", result);
        Assert.Contains("END:VEVENT", result);
    }

    [Fact]
    public void Generate_OmitsOptionalProperties_WhenNotProvided()
    {
        var ev = new IcsEvent
        {
            Uid = "x@y",
            StartUtc = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Utc),
            Summary = "Just a title",
        };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);
        Assert.DoesNotContain("DESCRIPTION:", result);
        Assert.DoesNotContain("LOCATION:", result);
    }

    [Fact]
    public void Generate_EscapesSpecialCharacters_Rfc5545()
    {
        var ev = new IcsEvent
        {
            Uid = "x@y",
            StartUtc = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Utc),
            Summary = "Comma, semi; backslash\\ end",
            Description = "Line one\nLine two",
        };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);
        Assert.Contains(@"SUMMARY:Comma\, semi\; backslash\\ end", result);
        Assert.Contains(@"DESCRIPTION:Line one\nLine two", result);
    }

    [Fact]
    public void Generate_UsesCrlfLineEndings_Rfc5545()
    {
        var result = IcsCalendarGenerator.Generate("ServantSync", Array.Empty<IcsEvent>(), Now);
        // Every \n must be preceded by \r. The simplest invariant: LF count
        // equals CR count. A bare \n (no preceding \r) would break this.
        Assert.Equal(result.Count(c => c == '\n'), result.Count(c => c == '\r'));
        Assert.Contains("VERSION:2.0\r\n", result);
    }

    [Fact]
    public void Generate_ConvertsUnspecifiedToUtc()
    {
        // The contract says StartUtc/EndUtc are UTC. The generator must
        // honor this even when the caller passes DateTimeKind.Unspecified
        // (the value the seeder / assigner produce). Without explicit
        // coercion, .NET's ToUniversalTime() would treat Unspecified as
        // local and shift the time. This test pins down the contract.
        var start = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Unspecified);
        var end   = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Unspecified);
        var ev = new IcsEvent { Uid = "1", StartUtc = start, EndUtc = end, Summary = "S" };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);
        Assert.Contains("DTSTART:20260709T140000Z", result);
        Assert.Contains("DTEND:20260709T160000Z", result);
    }

    [Fact]
    public void Generate_ConvertsLocalToUtc()
    {
        // A Local DateTime is converted to its UTC equivalent.
        var local = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Local);
        var ev = new IcsEvent
        {
            Uid = "1",
            StartUtc = local,
            EndUtc = local.AddHours(1),
            Summary = "S",
        };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);
        var expectedStart = local.ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        var expectedEnd   = local.AddHours(1).ToUniversalTime().ToString("yyyyMMddTHHmmssZ");
        Assert.Contains($"DTSTART:{expectedStart}", result);
        Assert.Contains($"DTEND:{expectedEnd}", result);
    }

    [Fact]
    public void Generate_MultipleEvents_AreEmittedInOrder()
    {
        var e1 = new IcsEvent { Uid = "a", StartUtc = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 7, 9, 15, 0, 0, DateTimeKind.Utc), Summary = "First" };
        var e2 = new IcsEvent { Uid = "b", StartUtc = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Utc), EndUtc = new DateTime(2026, 7, 9, 17, 0, 0, DateTimeKind.Utc), Summary = "Second" };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { e1, e2 }, Now);
        var firstIdx = result.IndexOf("SUMMARY:First", StringComparison.Ordinal);
        var secondIdx = result.IndexOf("SUMMARY:Second", StringComparison.Ordinal);
        Assert.True(firstIdx > 0);
        Assert.True(secondIdx > firstIdx, "Second event should appear after first.");
    }

    // ─── RFC 5545 §3.1 line folding (round X) ─────────────────────────
    // Content lines are folded at 75 octets with `\r\n ` continuation.
    // The fold is deterministic (octet count, NOT char count; UTF-8
    // multi-byte chars consume additional octets and force the boundary
    // back when needed).

    [Fact]
    public void Generate_FoldsContentLinesAt75Octets_Rfc5545()
    {
        // 150 ASCII 'A's + the 8-char "SUMMARY:" property prefix lands
        // us well above the 75-octet fold threshold on the first
        // segment. The RFC continuation marker is CRLF + single SPACE.
        var longSummary = new string('A', 150);
        var ev = new IcsEvent
        {
            Uid = "fold@test",
            StartUtc = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Utc),
            Summary = longSummary,
        };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);

        // At least one fold marker must be present.
        Assert.Contains("\r\n ", result);

        // No line, after splitting on CRLF, exceeds 75 octets.
        var lines = result.Split("\r\n", StringSplitOptions.None);
        foreach (var line in lines)
        {
            var octets = System.Text.Encoding.UTF8.GetByteCount(line);
            Assert.True(octets <= 75, $"RFC 5545 §3.1: line exceeds 75 octets ('{line}' is {octets}).");
        }
    }

    [Fact]
    public void Generate_FoldPreservesPayload_AfterReassembly()
    {
        // A conformant parser elides the leading SPACE on continuation
        // lines and rejoins. Equivalent re-assembly yields the original
        // property value. Validates the generator's fold math is
        // byte-for-byte exact (no char loss in the boundary).
        var longSummary = new string('A', 150);
        var ev = new IcsEvent
        {
            Uid = "fold-pres@test",
            StartUtc = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Utc),
            Summary = longSummary,
        };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);

        var lines = result.Split("\r\n", StringSplitOptions.None);
        var reassembled = "";
        var inProperty = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("SUMMARY:"))
            {
                reassembled = line.Substring("SUMMARY:".Length);
                inProperty = true;
                continue;
            }
            if (inProperty && line.StartsWith(" "))
            {
                // Continuation chunk: strip the leading SPACE and append.
                reassembled += line.Substring(1);
                continue;
            }
            inProperty = false;
        }

        Assert.Equal(longSummary, reassembled);
    }

    [Fact]
    public void Generate_FoldsByOctetCount_NotCharCount()
    {
        // RFC 5545 folds at 75 OCTETS, not 75 chars. A multibyte UTF-8
        // string uses more octets per char (the grinning-face emoji is
        // 4 bytes). 20 emojis = 80 chars but 80 octets; + 8-char
        // "SUMMARY:" prefix = 88 octets on the first segment, well
        // over the fold threshold even though the char count is far
        // past 75.
        var emojis = string.Empty;
        for (int i = 0; i < 20; i++) emojis += "\uD83D\uDE00";
        var ev = new IcsEvent
        {
            Uid = "fold-mb@test",
            StartUtc = new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc),
            EndUtc = new DateTime(2026, 7, 9, 16, 0, 0, DateTimeKind.Utc),
            Summary = emojis,
        };
        var result = IcsCalendarGenerator.Generate("ServantSync", new[] { ev }, Now);

        Assert.Contains("\r\n ", result);

        // Verify every emitted line is within the 75-octet cap even
        // though many chars are multibyte.
        var lines = result.Split("\r\n", StringSplitOptions.None);
        foreach (var line in lines)
        {
            var octets = System.Text.Encoding.UTF8.GetByteCount(line);
            Assert.True(octets <= 75, $"Multi-byte fold violated 75-octet cap on line '{line}' ({octets} octets).");
        }
    }
}
