using Microsoft.Extensions.Configuration;

namespace SchoolAdmission.Api.Configuration;

/// <summary>
/// Reservation duration configuration read from <c>HOLD_DURATION_SECONDS</c> at startup (R10).
/// </summary>
public sealed class HoldOptions
{
    public const string ConfigurationKey = "HOLD_DURATION_SECONDS";
    public const int DefaultDurationSeconds = 600;

    public int DurationSeconds { get; init; } = DefaultDurationSeconds;

    /// <summary>
    /// Reads and validates the duration. Throws <see cref="InvalidOperationException"/> when the value
    /// is present but not a positive integer, which aborts startup.
    /// </summary>
    public static HoldOptions FromConfiguration(IConfiguration configuration)
    {
        var raw = configuration[ConfigurationKey];

        if (raw is null)
        {
            return new HoldOptions();
        }

        if (!int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            || seconds <= 0)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} must be a positive integer number of seconds; received '{raw}'.");
        }

        return new HoldOptions { DurationSeconds = seconds };
    }
}
