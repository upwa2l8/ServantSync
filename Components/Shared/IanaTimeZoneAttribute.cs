using System.ComponentModel.DataAnnotations;

namespace ServantSync.Components.Shared;

/// <summary>
/// <see cref="ValidationAttribute"/> that confirms a string is either
/// <c>null</c>/empty or a recognized IANA timezone id (round-trippable
/// through <see cref="TimeZoneInfo.FindSystemTimeZoneById(string)"/>).
///
/// Round-AV: applied to <c>Organization.TimeZoneId</c> form-binding via
/// <c>OrgModel.TimeZoneId</c> on <c>Components/Pages/Organizations/Edit.razor</c>.
/// The picker constrains writes to a curated list
/// (<see cref="ServantSync.Services.TimeZoneOptions"/>), so the OS-side
/// check here is a backstop: if the curated list drifts out of sync with
/// the OS dictionary (or someone hand-edits the .razor file later), bad
/// ids are caught at validation time before they hit the DB. The
/// <see cref="Components.Shared.LocalTime"/> defensive
/// <c>catch (Exception)</c> on the fallback tier is the final safety net.
///
/// Null/empty is treated as VALID (the form maps the empty
/// <c>&lt;option value=""&gt;Use browser default&lt;/option&gt;</c> to a
/// null <c>TimeZoneId</c>, which means "no org-TZ override \u2014 fall back
/// to the user's browser zone").
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class IanaTimeZoneAttribute : ValidationAttribute
{
    public IanaTimeZoneAttribute()
        : base("The {0} field must be a recognized IANA timezone id (or empty to use the user's browser zone).")
    {
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        // Null / empty = "use browser default" (no org-TZ override), which
        // is the valid opt-out path. Both map to Organization.TimeZoneId = null
        // on save. Don't flag either as an error.
        if (value is null) return ValidationResult.Success;
        if (value is string s && string.IsNullOrWhiteSpace(s)) return ValidationResult.Success;

        if (value is not string id)
        {
            return new ValidationResult(
                "Timezone id must be a string.",
                new[] { validationContext.MemberName ?? string.Empty });
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(id);
            return ValidationResult.Success;
        }
        catch (TimeZoneNotFoundException)
        {
            // id isn't in the OS tz table.
            return new ValidationResult(
                FormatErrorMessage(validationContext.DisplayName ?? "TimezoneId"),
                new[] { validationContext.MemberName ?? string.Empty });
        }
        catch (InvalidTimeZoneException)
        {
            // id is structurally malformed (e.g. ASCII NULs, ICU-incompatible chars).
            return new ValidationResult(
                FormatErrorMessage(validationContext.DisplayName ?? "TimezoneId"),
                new[] { validationContext.MemberName ?? string.Empty });
        }
    }
}
