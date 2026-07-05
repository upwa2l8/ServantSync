// Returns the browser's IANA timezone identifier (e.g. "America/Chicago").
// Falls back to "UTC" if Intl is unavailable or throws.
export function getTimeZone() {
    try {
        const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
        return tz || "UTC";
    } catch {
        return "UTC";
    }
}
