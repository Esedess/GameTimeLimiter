using System.Globalization;
using System.Text.Json;
using GameLimiter.Core;
using Microsoft.Data.Sqlite;
namespace GameLimiter.Infrastructure;

public sealed class Store
{
    private readonly string connectionString;
    public Store(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, DefaultTimeout = 5 }.ToString();
        using var db = Open();
        Execute(db, "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;");
        var version = Convert.ToInt32(Scalar(db, "PRAGMA user_version;"));
        if (version > 2) throw new InvalidOperationException("База создана более новой версией GameLimiter.");
        Execute(db, """
            CREATE TABLE IF NOT EXISTS AppSettings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS Children(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, BaseSeconds INTEGER NOT NULL, Enabled INTEGER NOT NULL, Archived INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS Games(Id TEXT PRIMARY KEY, Json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS DailyAdjustments(ChildId INTEGER REFERENCES Children(Id), LocalDate TEXT, Seconds INTEGER NOT NULL, PRIMARY KEY(ChildId,LocalDate));
            CREATE TABLE IF NOT EXISTS UsageSessions(Id INTEGER PRIMARY KEY, ChildId INTEGER REFERENCES Children(Id), GameId TEXT NOT NULL, Started TEXT NOT NULL, Ended TEXT, Duration INTEGER NOT NULL DEFAULT 0, Reason TEXT, Checkpoint TEXT NOT NULL, ProcessId INTEGER, ProcessStart INTEGER, ReserveSeconds INTEGER NOT NULL DEFAULT 5);
            CREATE TABLE IF NOT EXISTS DailyUsage(SessionId INTEGER REFERENCES UsageSessions(Id), LocalDate TEXT, Seconds INTEGER NOT NULL, PRIMARY KEY(SessionId,LocalDate));
            """);
        if (version < 2)
        {
            using var migration = db.BeginTransaction();
            Execute(db, "ALTER TABLE UsageSessions ADD COLUMN SessionType TEXT NOT NULL DEFAULT 'CHILD'; UPDATE UsageSessions SET SessionType='ADULT_UNLIMITED' WHERE ChildId IS NULL; PRAGMA user_version=2;");
            migration.Commit();
        }
        Execute(db, "INSERT OR IGNORE INTO AppSettings VALUES('Timer',$0)", JsonSerializer.Serialize(new TimerSettings()));
    }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); return db; }
    private static SqliteCommand Command(SqliteConnection db, string sql, params object?[] values)
    {
        var cmd = db.CreateCommand(); cmd.CommandText = sql;
        for (var i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("$" + i, values[i] ?? DBNull.Value);
        return cmd;
    }
    private static void Execute(SqliteConnection db, string sql, params object?[] values) { using var cmd = Command(db, sql, values); cmd.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection db, string sql, params object?[] values) { using var cmd = Command(db, sql, values); return cmd.ExecuteScalar(); }
    public string? Get(string key) { using var db = Open(); return Scalar(db, "SELECT Value FROM AppSettings WHERE Key=$0", key) as string; }
    public void Set(string key, string value) { using var db = Open(); Execute(db, "INSERT INTO AppSettings VALUES($0,$1) ON CONFLICT(Key) DO UPDATE SET Value=$1", key, value); }
    public TimerSettings Settings => JsonSerializer.Deserialize<TimerSettings>(Get("Timer") ?? "{}")!;
    public void SaveSettings(TimerSettings value) { value.Validate(); Set("Timer", JsonSerializer.Serialize(value)); }
    public List<Child> Children(bool archived = false)
    {
        using var db = Open(); using var cmd = Command(db, "SELECT Id,Name,BaseSeconds,Enabled,Archived FROM Children WHERE Archived=0 OR $0=1 ORDER BY Id", archived); using var r = cmd.ExecuteReader();
        var result = new List<Child>(); while (r.Read()) result.Add(new(r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetBoolean(3), r.GetBoolean(4))); return result;
    }
    public Child SaveChild(Child child)
    {
        Validation.Child(child); using var db = Open();
        if (child.Id == 0) { Execute(db, "INSERT INTO Children(Name,BaseSeconds,Enabled,Archived) VALUES($0,$1,$2,$3)", child.Name, child.BaseSeconds, child.Enabled, child.Archived); return child with { Id = Convert.ToInt64(Scalar(db, "SELECT last_insert_rowid()")) }; }
        Execute(db, "UPDATE Children SET Name=$1,BaseSeconds=$2,Enabled=$3,Archived=$4 WHERE Id=$0", child.Id, child.Name, child.BaseSeconds, child.Enabled, child.Archived); return child;
    }
    public List<Game> Games() { using var db = Open(); using var cmd = Command(db, "SELECT Json FROM Games ORDER BY Id"); using var r = cmd.ExecuteReader(); var result = new List<Game>(); while (r.Read()) result.Add(JsonSerializer.Deserialize<Game>(r.GetString(0))!); return result; }
    public void SaveGame(Game game) { Validation.Game(game); using var db = Open(); Execute(db, "INSERT INTO Games VALUES($0,$1) ON CONFLICT(Id) DO UPDATE SET Json=$1", game.Id, JsonSerializer.Serialize(game)); }
    public void RemoveGame(string id) { using var db = Open(); Execute(db, "DELETE FROM Games WHERE Id=$0", id); }
    public void Adjust(long child, DateOnly date, int seconds)
    {
        if (Math.Abs((long)seconds) > 86400) throw new ArgumentException("Поправка: от -1440 до +1440 минут.");
        using var db = Open(); Execute(db, "INSERT INTO DailyAdjustments VALUES($0,$1,$2) ON CONFLICT(ChildId,LocalDate) DO UPDATE SET Seconds=Seconds+$2", child, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), seconds);
    }
    public Allowance Allowance(long child, DateOnly date)
    {
        using var db = Open(); var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new(Convert.ToInt64(Scalar(db, "SELECT CASE WHEN Enabled=1 AND Archived=0 THEN BaseSeconds ELSE 0 END FROM Children WHERE Id=$0", child)), Convert.ToInt64(Scalar(db, "SELECT Seconds FROM DailyAdjustments WHERE ChildId=$0 AND LocalDate=$1", child, day)), Convert.ToInt64(Scalar(db, "SELECT COALESCE(SUM(d.Seconds),0) FROM DailyUsage d JOIN UsageSessions s ON s.Id=d.SessionId WHERE s.ChildId=$0 AND d.LocalDate=$1", child, day)));
    }
    public long Begin(long? child, string game, DateTime now, int reserve, bool adultTimed = false)
    {
        using var db = Open(); Execute(db, "INSERT INTO UsageSessions(ChildId,GameId,Started,Checkpoint,ReserveSeconds,SessionType) VALUES($0,$1,$2,$2,$3,$4)", child, game, now.ToString("O"), reserve, child is not null ? "CHILD" : adultTimed ? "ADULT_TIMED" : "ADULT_UNLIMITED"); return Convert.ToInt64(Scalar(db, "SELECT last_insert_rowid()"));
    }
    public void Attach(long session, IGameProcess process) { using var db = Open(); Execute(db, "UPDATE UsageSessions SET ProcessId=$1,ProcessStart=$2 WHERE Id=$0", session, process.Id, process.StartUtcTicks); }
    // Each delta is committed once with the session checkpoint. Midnight is split locally.
    public void AddUsage(long session, DateTime from, int seconds, bool recovering = false)
    {
        if (seconds <= 0) return;
        using var db = Open(); using var tx = db.BeginTransaction(); var left = seconds; var cursor = from;
        while (left > 0)
        {
            var count = Math.Min(left, Math.Max(1, (int)Math.Ceiling((cursor.Date.AddDays(1) - cursor).TotalSeconds)));
            Execute(db, "INSERT INTO DailyUsage VALUES($0,$1,$2) ON CONFLICT(SessionId,LocalDate) DO UPDATE SET Seconds=Seconds+$2", session, cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), count);
            cursor = cursor.AddSeconds(count); left -= count;
        }
        Execute(db, "UPDATE UsageSessions SET Duration=Duration+$1,Checkpoint=$2 WHERE Id=$0", session, seconds, cursor.ToString("O"));
        if (recovering) Execute(db, "UPDATE UsageSessions SET ReserveSeconds=0 WHERE Id=$0", session);
        tx.Commit();
    }
    public void End(long id, string reason) { using var db = Open(); Execute(db, "UPDATE UsageSessions SET Ended=$1,Reason=$2 WHERE Id=$0", id, DateTime.Now.ToString("O"), reason); }
    public long Duration(long id) { using var db = Open(); return Convert.ToInt64(Scalar(db, "SELECT Duration FROM UsageSessions WHERE Id=$0", id)); }
    public List<(long Id, DateTime Checkpoint, int Reserve, int? Pid, long? Start)> Interrupted()
    {
        using var db = Open(); using var cmd = Command(db, "SELECT Id,Checkpoint,ReserveSeconds,ProcessId,ProcessStart FROM UsageSessions WHERE Ended IS NULL"); using var r = cmd.ExecuteReader(); var result = new List<(long, DateTime, int, int?, long?)>();
        while (r.Read()) result.Add((r.GetInt64(0), DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt64(4))); return result;
    }
}
