using AlleyWatch.Core.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AlleyWatch.Worker.Reporting;

/// <summary>Local SQLite log of every generated parking report — single file, no server, easy to inspect.</summary>
public sealed class ReportStore
{
    private readonly string _connectionString;

    public ReportStore(IOptionsMonitor<StorageOptions> options)
    {
        var dbPath = Path.GetFullPath(options.CurrentValue.DatabasePath);
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS ParkingReports (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ParkStartUtc TEXT NOT NULL,
                ParkEndUtc TEXT NOT NULL,
                VehicleColor TEXT,
                VehicleMakeModel TEXT,
                LicensePlateText TEXT,
                PeopleActivityDescription TEXT,
                CreatedUtc TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    public async Task InsertAsync(
        DateTimeOffset parkStartUtc,
        DateTimeOffset parkEndUtc,
        VehicleReportResult report,
        CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ParkingReports
                (ParkStartUtc, ParkEndUtc, VehicleColor, VehicleMakeModel, LicensePlateText, PeopleActivityDescription, CreatedUtc)
            VALUES
                ($parkStart, $parkEnd, $color, $makeModel, $plate, $people, $created);
            """;
        command.Parameters.AddWithValue("$parkStart", parkStartUtc.ToString("o"));
        command.Parameters.AddWithValue("$parkEnd", parkEndUtc.ToString("o"));
        command.Parameters.AddWithValue("$color", report.VehicleColor);
        command.Parameters.AddWithValue("$makeModel", (object?)report.VehicleMakeModel ?? DBNull.Value);
        command.Parameters.AddWithValue("$plate", (object?)report.LicensePlateText ?? DBNull.Value);
        command.Parameters.AddWithValue("$people", report.PeopleActivityDescription);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("o"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
