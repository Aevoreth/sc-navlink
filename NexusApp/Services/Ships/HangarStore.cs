using System.IO;
using Microsoft.Data.Sqlite;

namespace NexusApp.Services;

/// <summary>
/// Durable My Hangar rows in nexus.db. Path is injectable so tests do not touch the user profile.
/// </summary>
internal sealed class HangarStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _gate = new();
    private bool _disposed;

    public HangarStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        Exec("PRAGMA busy_timeout=3000;");
        Exec(@"
            CREATE TABLE IF NOT EXISTS hangar_ships (
                id TEXT PRIMARY KEY,
                catalog_id TEXT NOT NULL,
                acquisition INTEGER NOT NULL,
                rental_expires_utc TEXT,
                last_known_location TEXT,
                provenance TEXT NOT NULL,
                added_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_hangar_catalog ON hangar_ships(catalog_id);
        ");
    }

    public IReadOnlyList<HangarEntry> List()
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"SELECT id, catalog_id, acquisition, rental_expires_utc,
                last_known_location, provenance, added_utc
                FROM hangar_ships ORDER BY added_utc DESC";
            using var r = cmd.ExecuteReader();
            var list = new List<HangarEntry>();
            while (r.Read())
                list.Add(Read(r));
            return list;
        }
    }

    public HangarEntry? ByCatalogId(string catalogId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"SELECT id, catalog_id, acquisition, rental_expires_utc,
                last_known_location, provenance, added_utc
                FROM hangar_ships WHERE catalog_id = @id LIMIT 1";
            cmd.Parameters.AddWithValue("@id", catalogId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? Read(r) : null;
        }
    }

    public HangarEntry Upsert(HangarEntry entry)
    {
        lock (_gate)
        {
            Exec(@"
                INSERT INTO hangar_ships(id, catalog_id, acquisition, rental_expires_utc,
                    last_known_location, provenance, added_utc)
                VALUES (@id, @cid, @acq, @exp, @loc, @prov, @added)
                ON CONFLICT(id) DO UPDATE SET
                    catalog_id = excluded.catalog_id,
                    acquisition = excluded.acquisition,
                    rental_expires_utc = excluded.rental_expires_utc,
                    last_known_location = excluded.last_known_location,
                    provenance = excluded.provenance;",
                ("@id", entry.Id),
                ("@cid", entry.CatalogId),
                ("@acq", (int)entry.Acquisition),
                ("@exp", (object?)entry.RentalExpiresUtc?.ToString("O") ?? DBNull.Value),
                ("@loc", (object?)entry.LastKnownLocation ?? DBNull.Value),
                ("@prov", entry.Provenance),
                ("@added", entry.AddedUtc.ToString("O")));
            return entry;
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM hangar_ships WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn.Dispose();
    }

    private static HangarEntry Read(SqliteDataReader r) =>
        new(
            r.GetString(0),
            r.GetString(1),
            (HangarAcquisition)r.GetInt32(2),
            r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.GetString(5),
            DateTime.Parse(r.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind));

    private void Exec(string sql, params (string, object)[] parms)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }
}
