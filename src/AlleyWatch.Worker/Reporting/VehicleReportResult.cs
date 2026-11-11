using System.Text.Json.Serialization;

namespace AlleyWatch.Worker.Reporting;

/// <summary>Claude's structured-output report for one parking event. Matches ClaudeReportClient's JSON schema.</summary>
public sealed record VehicleReportResult(
    [property: JsonPropertyName("vehicleColor")] string VehicleColor,
    [property: JsonPropertyName("vehicleMakeModel")] string? VehicleMakeModel,
    [property: JsonPropertyName("licensePlateText")] string? LicensePlateText,
    [property: JsonPropertyName("peopleActivityDescription")] string PeopleActivityDescription);
