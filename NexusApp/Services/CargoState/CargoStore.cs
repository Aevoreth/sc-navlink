using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace NexusApp.Services;

/// <summary>
/// Durable cargo lots in nexus.db. Path is injectable so tests do not touch the user profile.
/// </summary>
internal sealed class CargoStore : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly object _gate = new();
    private bool _disposed;

    public CargoStore(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        Exec("PRAGMA busy_timeout=3000;");
        Exec(@"
            CREATE TABLE IF NOT EXISTS cargo_lots (
                id TEXT PRIMARY KEY,
                ship_id TEXT NOT NULL,
                commodity TEXT NOT NULL,
                uex_commodity_id INTEGER,
                scu INTEGER NOT NULL,
                container_scu INTEGER,
                container_count INTEGER,
                provenance TEXT NOT NULL,
                confidence REAL,
                observed_utc TEXT NOT NULL,
                off_grid INTEGER NOT NULL DEFAULT 0,
                contracted INTEGER NOT NULL DEFAULT 0,
                destination TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS ix_cargo_ship ON cargo_lots(ship_id);
        ");
        try { Exec("ALTER TABLE cargo_lots ADD COLUMN off_grid INTEGER NOT NULL DEFAULT 0"); }
        catch (SqliteException) { /* already present */ }
        try { Exec("ALTER TABLE cargo_lots ADD COLUMN contracted INTEGER NOT NULL DEFAULT 0"); }
        catch (SqliteException) { /* already present */ }
        try { Exec("ALTER TABLE cargo_lots ADD COLUMN destination TEXT NOT NULL DEFAULT ''"); }
        catch (SqliteException) { /* already present */ }
    }

    public IReadOnlyList<GameCargoLot> List()
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"SELECT id, ship_id, commodity, uex_commodity_id, scu,
                container_scu, container_count, provenance, confidence, observed_utc, off_grid, contracted, destination
                FROM cargo_lots ORDER BY observed_utc DESC";
            using var r = cmd.ExecuteReader();
            var list = new List<GameCargoLot>();
            while (r.Read())
                list.Add(Read(r));
            return list;
        }
    }

    public IReadOnlyList<GameCargoLot> ListByShip(string shipId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"SELECT id, ship_id, commodity, uex_commodity_id, scu,
                container_scu, container_count, provenance, confidence, observed_utc, off_grid, contracted, destination
                FROM cargo_lots WHERE ship_id = @sid ORDER BY observed_utc DESC";
            cmd.Parameters.AddWithValue("@sid", shipId);
            using var r = cmd.ExecuteReader();
            var list = new List<GameCargoLot>();
            while (r.Read())
                list.Add(Read(r));
            return list;
        }
    }

    public GameCargoLot Upsert(GameCargoLot lot)
    {
        lock (_gate)
        {
            Exec(@"
                INSERT INTO cargo_lots(id, ship_id, commodity, uex_commodity_id, scu,
                    container_scu, container_count, provenance, confidence, observed_utc, off_grid, contracted, destination)
                VALUES (@id, @sid, @name, @uex, @scu, @box, @count, @prov, @conf, @obs, @off, @con, @dest)
                ON CONFLICT(id) DO UPDATE SET
                    ship_id = excluded.ship_id,
                    commodity = excluded.commodity,
                    uex_commodity_id = excluded.uex_commodity_id,
                    scu = excluded.scu,
                    container_scu = excluded.container_scu,
                    container_count = excluded.container_count,
                    provenance = excluded.provenance,
                    confidence = excluded.confidence,
                    observed_utc = excluded.observed_utc,
                    off_grid = excluded.off_grid,
                    contracted = excluded.contracted,
                    destination = excluded.destination;",
                ("@id", lot.Id),
                ("@sid", lot.ShipId),
                ("@name", lot.Commodity),
                ("@uex", (object?)lot.UexCommodityId ?? DBNull.Value),
                ("@scu", lot.Scu),
                ("@box", (object?)lot.ContainerScu ?? DBNull.Value),
                ("@count", (object?)lot.ContainerCount ?? DBNull.Value),
                ("@prov", lot.Provenance.ToString()),
                ("@conf", (object?)lot.Confidence ?? DBNull.Value),
                ("@obs", lot.ObservedUtc.ToUniversalTime().ToString("O")),
                ("@off", lot.OffGrid ? 1 : 0),
                ("@con", lot.Contracted ? 1 : 0),
                ("@dest", lot.Destination ?? ""));
            return lot;
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM cargo_lots WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    public int DeleteByShip(string shipId)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM cargo_lots WHERE ship_id = @sid";
            cmd.Parameters.AddWithValue("@sid", shipId);
            return cmd.ExecuteNonQuery();
        }
    }

    public int DeleteByShipCommodity(string shipId, string commodity)
    {
        lock (_gate)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM cargo_lots WHERE ship_id = @sid AND commodity = @name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@sid", shipId);
            cmd.Parameters.AddWithValue("@name", commodity);
            return cmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn.Dispose();
    }

    private static GameCargoLot Read(SqliteDataReader r)
    {
        var provenance = Enum.TryParse<GameCargoProvenance>(r.GetString(7), out var p)
            ? p
            : GameCargoProvenance.UserConfirmed;
        var observed = DateTime.Parse(r.GetString(9), null, DateTimeStyles.RoundtripKind);
        if (observed.Kind == DateTimeKind.Unspecified)
            observed = DateTime.SpecifyKind(observed, DateTimeKind.Utc);
        return new GameCargoLot(
            r.GetString(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetInt32(3),
            r.GetInt32(4),
            r.IsDBNull(5) ? null : r.GetInt32(5),
            r.IsDBNull(6) ? null : r.GetInt32(6),
            provenance,
            r.IsDBNull(8) ? null : r.GetDouble(8),
            observed.ToUniversalTime(),
            r.FieldCount > 10 && !r.IsDBNull(10) && r.GetInt32(10) != 0,
            r.FieldCount > 11 && !r.IsDBNull(11) && r.GetInt32(11) != 0,
            r.FieldCount > 12 && !r.IsDBNull(12) ? r.GetString(12) ?? "" : "");
    }

    private void Exec(string sql, params (string, object)[] parms)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }
}
