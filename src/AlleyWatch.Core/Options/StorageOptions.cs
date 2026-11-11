namespace AlleyWatch.Core.Options;

/// <summary>Bound from the "Storage" section of appsettings.json.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string DatabasePath { get; set; } = "data\\alleywatch.db";
}
