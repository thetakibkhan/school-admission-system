using System.Text.Json.Serialization;

namespace SchoolAdmission.Api.Dtos;

/// <summary>Body of <c>POST /classes</c>. Field names are fixed by the API contract (R1).</summary>
public sealed record CreateClassRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("totalSeats")] int? TotalSeats);

/// <summary>Body of a successful <c>POST /classes</c> (201).</summary>
public sealed record ClassCreatedResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("totalSeats")] int TotalSeats);

/// <summary>One class with its derived seat counts, for both class GET endpoints.</summary>
public sealed record ClassResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("totalSeats")] int TotalSeats,
    [property: JsonPropertyName("heldSeats")] int HeldSeats,
    [property: JsonPropertyName("confirmedSeats")] int ConfirmedSeats,
    [property: JsonPropertyName("availableSeats")] int AvailableSeats);
