using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.Dtos;

/// <summary>
/// Writes every timestamp as ISO 8601 UTC with a trailing <c>Z</c> (R5), whatever the
/// <see cref="DateTime.Kind"/> the database driver produced.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        AsUtc(reader.GetDateTime());

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(AsUtc(value).ToString(Format, CultureInfo.InvariantCulture));

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}

public sealed record HoldCreatedResponse(
    string HoldId,
    string ClassId,
    string StudentId,
    string Status,
    [property: JsonConverter(typeof(UtcDateTimeJsonConverter))] DateTime ExpiresAt);

public sealed record HoldResponse(
    string HoldId,
    string ClassId,
    string Status,
    [property: JsonConverter(typeof(UtcDateTimeJsonConverter))] DateTime ExpiresAt);

public sealed record ConfirmResponse(string HoldId, string Status);

public sealed record MyHoldResponse(
    string HoldId,
    string ClassId,
    string ClassName,
    string Status,
    [property: JsonConverter(typeof(UtcDateTimeJsonConverter))] DateTime ExpiresAt);
