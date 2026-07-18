using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ServantSync.Components.Shared;

namespace ServantSync.Services.CalendarPdf;

/// <summary>
/// QuestPDF implementation of <see cref="ICalendarPdfBuilder"/>.
/// Generates printable A4 calendar PDFs with cover band (text wordmark,
/// org name, slot name, date/tz info, org-join QR), month / week / day
/// body layouts, per-occurrence QR codes, and a footer.
/// </summary>
public class QuestPdfCalendarPdfBuilder : ICalendarPdfBuilder
{
    private static readonly string BrandPurple = "#3730a3";
    private const float HourRowHeight = 12f;

    public async Task BuildAsync(CalendarPdfRequest request, IQrCodeBuilder qrCodeBuilder,
        Stream output, string? culture = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var cultureInfo = string.IsNullOrEmpty(culture)
            ? CultureInfo.CurrentUICulture
            : new CultureInfo(culture);

        // Resolve timezone for display.
        var tz = TimeZoneResolver.Resolve(
            request.TimeZoneDisplayName, request.TimeZoneDisplayName);

        // Generate org-join QR code image bytes.
        byte[]? orgJoinQrBytes = null;
        if (!string.IsNullOrEmpty(request.OrgJoinUrl))
            orgJoinQrBytes = qrCodeBuilder.GeneratePng(request.OrgJoinUrl);

        // Generate per-occurrence QR codes for open slots.
        // Use BaseUri (always set by the endpoint) rather than OrgJoinUrl
        // so QRs are generated even when the org has no registration token.
        var occurrenceQrMap = new Dictionary<int, byte[]>();
        foreach (var occ in request.Occurrences.Where(o => o.IsOpen))
        {
            var url = !string.IsNullOrEmpty(request.BaseUri)
                ? $"{request.BaseUri}/Open?occ={occ.Id}"
                : null;
            if (url is null) continue;
            var qrBytes = qrCodeBuilder.GeneratePng(url);
            if (qrBytes is not null)
                occurrenceQrMap[occ.Id] = qrBytes;
        }

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(15, Unit.Millimetre);

                page.Header().Element(c => ComposeCoverBand(c, request, orgJoinQrBytes, tz, cultureInfo));
                page.Content().Element(c => ComposeBody(c, request, occurrenceQrMap, tz, cultureInfo));
                page.Footer().Element(c => ComposeFooter(c, request));
            });
        });

        doc.GeneratePdf(output);
        await output.FlushAsync();
    }

    private void ComposeCoverBand(IContainer container, CalendarPdfRequest request,
        byte[]? orgJoinQrBytes, TimeZoneInfo tz, CultureInfo culture)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem(3).Column(left =>
                {
                    // Text wordmark — "ServantSync" in brand purple.
                    left.Item().Text("ServantSync")
                        .FontSize(22).Bold().FontColor(BrandPurple);

                    left.Item().PaddingTop(4).Text(request.OrganizationName)
                        .FontSize(14).SemiBold();

                    left.Item().PaddingTop(2).Text(request.SlotName)
                        .FontSize(11);

                    var dateStr = request.StartDate.ToString("MMM d, yyyy", culture);
                    var tzName = tz.DisplayName;
                    left.Item().PaddingTop(4).Text($"As of {dateStr}  ·  Times shown in {tzName}")
                        .FontSize(8).FontColor(Colors.Grey.Medium);
                });

                // Org-join QR in bottom-right of cover band.
                row.RelativeItem(1).AlignRight().AlignMiddle().Column(right =>
                {
                    if (orgJoinQrBytes is not null)
                    {
                        right.Item().Width(45, Unit.Millimetre).Image(orgJoinQrBytes);
                        right.Item().PaddingTop(2).Text($"Scan to join {request.OrganizationName}")
                            .FontSize(6).FontColor(Colors.Grey.Medium).AlignCenter();
                    }
                    else
                    {
                        right.Item().Text("Ask a coordinator\nfor the invite link")
                            .FontSize(7).FontColor(Colors.Grey.Medium).AlignCenter();
                    }
                });
            });

            // Thin brand-purple rule.
            col.Item().PaddingTop(4).LineHorizontal(1).LineColor(BrandPurple);
        });
    }

    private void ComposeBody(IContainer container, CalendarPdfRequest request,
        Dictionary<int, byte[]> occurrenceQrMap, TimeZoneInfo tz, CultureInfo culture)
    {
        container.PaddingTop(8).Column(col =>
        {
            col.Item().Element(c =>
            {
                switch (request.Scope)
                {
                    case CalendarScope.Week:
                        ComposeWeekView(c, request, occurrenceQrMap, tz, culture);
                        break;
                    case CalendarScope.Day:
                        ComposeDayView(c, request, occurrenceQrMap, tz, culture);
                        break;
                    default:
                        ComposeMonthView(c, request, occurrenceQrMap, tz, culture);
                        break;
                }
            });

            if (request.Occurrences.Count == 0)
            {
                col.Item().PaddingTop(20).AlignCenter().Text(
                    "No shifts scheduled in this window.\nCheck back after the coordinator adds the next round of assignments.")
                    .FontSize(10).FontColor(Colors.Grey.Medium);
            }
        });
    }

    private void ComposeMonthView(IContainer container, CalendarPdfRequest request,
        Dictionary<int, byte[]> occurrenceQrMap, TimeZoneInfo tz, CultureInfo culture)
    {
        var startDate = request.StartDate;
        var year = startDate.Year;
        var month = startDate.Month;
        var firstDay = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var startDow = (int)firstDay.DayOfWeek; // 0=Sun

        // Group occurrences by local date.
        var byDate = request.Occurrences
            .GroupBy(o => TimeZoneResolver.ToLocal(o.StartUtc, tz).Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Available content height on A4 ≈ 267 mm (297 – 30 mm margins).
        // Header/footer may take ~50 mm, leaving ~217 mm for content.
        // Reserve ~16 mm for the key row at the bottom → ~200 mm for the grid.
        // Dynamic cell height: cap at 40 mm for 5-row months, shrink for 6-row months.
        int totalCells = startDow + daysInMonth;
        int numRows = (int)Math.Ceiling(totalCells / 7.0);
        float monthCellHeight = Math.Min(40f, 200f / numRows);

        container.Column(col =>
        {
            // Day-of-week header row.
            col.Item().Table(headerTable =>
            {
                headerTable.ColumnsDefinition(edef =>
                {
                    for (int i = 0; i < 7; i++) edef.RelativeColumn();
                });
                var dayNames = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
                foreach (var dn in dayNames)
                    headerTable.Cell().PaddingBottom(2).Text(dn).FontSize(8)
                        .SemiBold().FontColor(BrandPurple).AlignCenter();
            });

            // Day cells grid — 7 columns, 5 or 6 rows.
            for (int row = 0; row < numRows; row++)
            {
                col.Item().Table(cellTable =>
                {
                    cellTable.ColumnsDefinition(edef =>
                    {
                        for (int i = 0; i < 7; i++) edef.RelativeColumn();
                    });

                    for (int colIdx = 0; colIdx < 7; colIdx++)
                    {
                        int cellIndex = row * 7 + colIdx;
                        int dayNum = cellIndex - startDow + 1;

                        if (dayNum < 1 || dayNum > daysInMonth)
                        {
                            cellTable.Cell().Height(monthCellHeight, Unit.Millimetre).Border(0.5f)
                                .BorderColor(Colors.Grey.Lighten2);
                            continue;
                        }

                        var cellDate = new DateTime(year, month, dayNum, 0, 0, 0, DateTimeKind.Unspecified);
                        var dayOccurrences = byDate.TryGetValue(cellDate, out var list) ? list : new();

                        cellTable.Cell().Height(monthCellHeight, Unit.Millimetre).Border(0.5f)
                            .BorderColor(Colors.Grey.Lighten2).Padding(2).Column(cellCol =>
                        {
                            cellCol.Item().Text(dayNum.ToString()).FontSize(8).SemiBold();

                            int shown = 0;
                            foreach (var occ in dayOccurrences.OrderBy(o => o.StartUtc).Take(3))
                            {
                                shown++;
                                var localStart = TimeZoneResolver.ToLocal(occ.StartUtc, tz);
                                var localEnd = TimeZoneResolver.ToLocal(occ.EndUtc, tz);
                                var timeStr = $"{localStart:HH:mm}–{localEnd:HH:mm}";
                                var icon = occ.IsOpen ? "○" : "●";

                                cellCol.Item().Row(r =>
                                {
                                    r.AutoItem().Text(icon).FontSize(6)
                                        .FontColor(occ.IsOpen ? Colors.Grey.Medium : Colors.Green.Medium);
                                    r.AutoItem().PaddingLeft(1).Text(timeStr).FontSize(6);
                                });
                            }

                            int overflow = dayOccurrences.Count - shown;
                            if (overflow > 0)
                                cellCol.Item().Text($"({overflow} more)").FontSize(5)
                                    .FontColor(Colors.Grey.Medium);
                        });
                    }
                });
            }

            // Key row.
            col.Item().PaddingTop(4).Row(row =>
            {
                row.AutoItem().Text("○ Open").FontSize(7).FontColor(Colors.Grey.Medium);
                row.AutoItem().PaddingLeft(8).Text("● Filled").FontSize(7).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private void ComposeWeekView(IContainer container, CalendarPdfRequest request,
        Dictionary<int, byte[]> occurrenceQrMap, TimeZoneInfo tz, CultureInfo culture)
    {
        // Group occurrences by local date.
        var byDate = request.Occurrences
            .GroupBy(o => TimeZoneResolver.ToLocal(o.StartUtc, tz).Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Available content height on A4 ≈ 267 mm (297 – 30 mm margins).
        // Header/footer may take ~50 mm, leaving ~217 mm for content.
        // 16 hours × 12 mm = 192 mm fits comfortably.
        int startHour = 6, endHour = 21;
        var weekStart = request.StartDate.Date;

        container.Column(col =>
        {
            // Header row with day names + dates.
            col.Item().Table(hdr =>
            {
                hdr.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(35); // time column
                    for (int d = 0; d < 7; d++) cols.RelativeColumn();
                });

                hdr.Cell().Text("Time").FontSize(7).SemiBold().FontColor(BrandPurple);
                for (int d = 0; d < 7; d++)
                {
                    var dt = weekStart.AddDays(d);
                    hdr.Cell().Text($"{dt:ddd}\n{dt:d}").FontSize(7).SemiBold()
                        .FontColor(BrandPurple).AlignCenter();
                }
            });

            // Hourly rows.
            for (int h = startHour; h <= endHour; h++)
            {
                col.Item().Table(hourRow =>
                {
                    hourRow.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(35);
                        for (int d = 0; d < 7; d++) cols.RelativeColumn();
                    });

                    hourRow.Cell().Height(HourRowHeight, Unit.Millimetre).Border(0.5f)
                        .BorderColor(Colors.Grey.Lighten2).AlignMiddle()
                        .Text($"{h:00}:00").FontSize(6).FontColor(Colors.Grey.Medium);

                    for (int d = 0; d < 7; d++)
                    {
                        var cellDate = weekStart.AddDays(d);
                        var hourOccurrences = byDate.TryGetValue(cellDate, out var list)
                            ? list.Where(o => TimeZoneResolver.ToLocal(o.StartUtc, tz).Hour == h).ToList()
                            : new();

                        hourRow.Cell().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .Padding(1).Column(cellCol =>
                        {
                            foreach (var occ in hourOccurrences.Take(2))
                            {
                                var localStart = TimeZoneResolver.ToLocal(occ.StartUtc, tz);
                                var localEnd = TimeZoneResolver.ToLocal(occ.EndUtc, tz);
                                cellCol.Item().Text($"{localStart:HH:mm}–{localEnd:HH:mm}")
                                    .FontSize(5).FontColor(occ.IsOpen ? Colors.Grey.Darken1 : Colors.Green.Darken1);
                            }
                        });
                    }
                });
            }
        });
    }

    private void ComposeDayView(IContainer container, CalendarPdfRequest request,
        Dictionary<int, byte[]> occurrenceQrMap, TimeZoneInfo tz, CultureInfo culture)
    {
        // Available content height on A4 ≈ 267 mm (297 – 30 mm margins).
        // Header/footer may take ~50 mm, leaving ~217 mm for content.
        // 16 hours × 12 mm = 192 mm fits comfortably.
        int startHour = 6, endHour = 21;
        var dayDate = request.StartDate.Date;

        container.Column(col =>
        {
            // Day header.
            col.Item().PaddingBottom(4).Text($"{dayDate:dddd, MMMM d, yyyy}")
                .FontSize(12).SemiBold().FontColor(BrandPurple);

            for (int h = startHour; h <= endHour; h++)
            {
                var hourOccurrences = request.Occurrences
                    .Where(o =>
                    {
                        var local = TimeZoneResolver.ToLocal(o.StartUtc, tz);
                        return local.Date == dayDate && local.Hour == h;
                    })
                    .OrderBy(o => o.StartUtc)
                    .ToList();

                col.Item().Row(row =>
                {
                    // Time label.
                    row.ConstantItem(35, Unit.Millimetre).Height(HourRowHeight, Unit.Millimetre)
                        .Border(0.5f).BorderColor(Colors.Grey.Lighten2).AlignMiddle()
                        .Text($"{h:00}:00").FontSize(7).FontColor(Colors.Grey.Medium);

                    // Timeline content.
                    row.RelativeItem().Border(0.5f).BorderColor(Colors.Grey.Lighten2)
                        .Height(HourRowHeight, Unit.Millimetre).Padding(2).Row(tlRow =>
                    {
                        if (hourOccurrences.Count == 0)
                        {
                            tlRow.RelativeItem().AlignMiddle().Text("").FontSize(6);
                        }
                        else
                        {
                            foreach (var occ in hourOccurrences)
                            {
                                var localStart = TimeZoneResolver.ToLocal(occ.StartUtc, tz);
                                var localEnd = TimeZoneResolver.ToLocal(occ.EndUtc, tz);
                                var label = occ.IsOpen
                                    ? $"{localStart:HH:mm}–{localEnd:HH:mm}  open"
                                    : $"{localStart:HH:mm}–{localEnd:HH:mm}  filled";
                                if (request.ShowVolunteerNames && !occ.IsOpen)
                                    label += $" — {occ.AssignedVolunteerName}";

                                tlRow.RelativeItem().AlignMiddle().Text(label)
                                    .FontSize(6).FontColor(occ.IsOpen
                                        ? Colors.Grey.Darken1
                                        : Colors.Green.Darken1);

                                // QR code for open occurrences.
                                if (occ.IsOpen && occurrenceQrMap.TryGetValue(occ.Id, out var qrBytes))
                                {
                                    tlRow.ConstantItem(12, Unit.Millimetre).AlignMiddle()
                                        .Image(qrBytes);
                                }
                            }
                        }
                    });
                });
            }
        });
    }

    private void ComposeFooter(IContainer container, CalendarPdfRequest request)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span($"Generated {request.GeneratedUtc:u}  ·  Verify availability before traveling  ·  ServantSync calendar")
                .FontSize(7).FontColor(Colors.Grey.Medium);
        });
    }
}
