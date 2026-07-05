namespace ServantSync.Services;

/// <summary>
/// Holds the current user's IANA timezone identifier, populated once per
/// Blazor circuit by <see cref="Components.TimeZoneInitializer"/>. Default
/// null during prerender; consumers fall back to <see cref="TimeZoneInfo.Local"/>
/// when this is unset.
/// </summary>
public class UserTimeZoneProvider
{
    /// <summary>IANA timezone id (e.g. "America/Chicago") or null.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>True once the initializer has either populated or unfilled this provider.</summary>
    public bool Initialized { get; set; }

    /// <summary>
    /// Raised right after <see cref="TimeZoneId"/> is set on the first
    /// initialization round-trip. Subscribers (e.g. <c>LocalTime</c>, which
    /// can't easily tell Blazor "the dependency changed, re-render me")
    /// listen for this and call <c>StateHasChanged()</c> so the first
    /// server-local fallback gets replaced with the user's actual zone.
    /// </summary>
    public event Action? TimeZoneChanged;

    /// <summary>Invoker for <see cref="TimeZoneChanged"/>. Called by
    /// <c>TimeZoneInitializer</c> after it assigns <see cref="TimeZoneId"/>.
    /// The null-conditional handles the (unlikely) case of a subscriber
    /// detaching between the field write and this call.</summary>
    public void RaiseChanged() => TimeZoneChanged?.Invoke();

    /// <summary>Returns a <see cref="DateTimeKind.Unspecified"/> local
    /// representation of <paramref name="utc"/> in the user's browser zone
    /// (or server <see cref="TimeZoneInfo.Local"/> during the prerender
    /// window before the JS interop call populates <see cref="TimeZoneId"/>).
    /// Callers compose this with <c>.Date</c> for day bucketing or
    /// <c>.ToString("HH:mm")</c> for time input seeds.</summary>
    public DateTime ToLocal(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var zone = !string.IsNullOrEmpty(TimeZoneId)
            ? TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId)
            : TimeZoneInfo.Local;
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }
}
