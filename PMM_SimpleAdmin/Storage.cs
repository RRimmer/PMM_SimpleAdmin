using System.Data.Common;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace PMM_SimpleAdmin;

// Menu style per player in a database. The connection comes only from CS2-SimpleAdmin's config
// (configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json, DatabaseConfig):
//   MySQL  -> table pmm_sa_players in SimpleAdmin's MySQL database (shared by all servers on it);
//   SQLite -> an own file configs/plugins/PMM_SimpleAdmin/pmm_simpleadmin.sqlite. Never SimpleAdmin's .sqlite:
//             SimpleAdmin uses System.Data.SQLite, this module e_sqlite3 - two SQLite copies in one process
//             on one file break each other's locks.
// Every query runs on a worker thread; results come back through the callbacks (the caller moves them to the game thread).
internal sealed class StyleStore
{
    private const string Table = "pmm_sa_players";

    private readonly ILogger _log;
    private readonly string _moduleDir;
    private readonly string _configsDir;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string _connection = "";
    private bool _mysql;
    private volatile bool _ready;

    public string Describe { get; private set; } = "not started";

    public StyleStore(ILogger log, string moduleDirectory)
    {
        _log = log;
        _moduleDir = moduleDirectory;
        _configsDir = Path.GetFullPath(Path.Combine(moduleDirectory, "..", "..", "configs", "plugins"));
    }

    // Opens the database, creates the table, moves an old players.json in and loads every saved style.
    public void Start(Action<Dictionary<ulong, string>> loaded)
    {
        try
        {
            ReadSimpleAdminConfig();
        }
        catch (Exception e)
        {
            Describe = "error: " + e.Message;
            _log.LogError(e, "Menu styles: cannot read the database settings of CS2-SimpleAdmin");
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await Exec($"CREATE TABLE IF NOT EXISTS {Table} (steamid BIGINT NOT NULL PRIMARY KEY, style VARCHAR(16) NOT NULL, updated_at BIGINT NOT NULL)");
                _ready = true;
                await ImportJson();
                var all = await LoadAll();
                _log.LogInformation("Menu styles: {Kind}, {Count} players", Describe, all.Count);
                loaded(all);
            }
            catch (Exception e)
            {
                _ready = false;
                Describe += " (error: " + e.Message + ")";
                _log.LogError(e, "Menu styles: database {Kind} is not available, styles live until the map changes", Describe);
            }
        });
    }

    // One player (on connect): another server on the same MySQL may have changed the style.
    public void LoadOne(ulong steamId, Action<string?> done)
    {
        if (!_ready) return;
        Task.Run(async () =>
        {
            try
            {
                await _lock.WaitAsync();
                try
                {
                    await using var conn = await Open();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT style FROM {Table} WHERE steamid = @id";
                    Add(cmd, "@id", (long)steamId);
                    done(await cmd.ExecuteScalarAsync() as string);
                }
                finally { _lock.Release(); }
            }
            catch (Exception e)
            {
                _log.LogWarning("Menu styles: load {Id} failed: {Error}", steamId, e.Message);
            }
        });
    }

    public void Save(ulong steamId, string style)
    {
        if (!_ready) return;
        Task.Run(async () =>
        {
            try
            {
                await Upsert(steamId, style);
            }
            catch (Exception e)
            {
                _log.LogWarning("Menu styles: save {Id} failed: {Error}", steamId, e.Message);
            }
        });
    }

    // ---------------- connection ----------------

    private void ReadSimpleAdminConfig()
    {
        string path = Path.Combine(_configsDir, "CS2-SimpleAdmin", "CS2-SimpleAdmin.json");
        string type = "SQLite", host = "", user = "", password = "", name = "", ssl = "preferred";
        uint port = 3306;

        if (File.Exists(path))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (doc.RootElement.TryGetProperty("DatabaseConfig", out var db) && db.ValueKind == JsonValueKind.Object)
            {
                type = Str(db, "DatabaseType", type);
                host = Str(db, "DatabaseHost", host);
                user = Str(db, "DatabaseUser", user);
                password = Str(db, "DatabasePassword", password);
                name = Str(db, "DatabaseName", name);
                ssl = Str(db, "DatabaseSSlMode", ssl);
                if (db.TryGetProperty("DatabasePort", out var p) && p.TryGetUInt32(out uint pv)) port = pv;
            }
        }
        else
        {
            _log.LogWarning("Menu styles: {Path} not found, using SQLite", path);
        }

        if (type.Contains("mysql", StringComparison.OrdinalIgnoreCase) && host != "" && user != "" && name != "")
        {
            var builder = new MySqlConnectionStringBuilder
            {
                Server = host,
                Port = port,
                UserID = user,
                Password = password,
                Database = name,
                Pooling = true,
            };
            if (Enum.TryParse<MySqlSslMode>(ssl, true, out var mode)) builder.SslMode = mode;
            _connection = builder.ConnectionString;
            _mysql = true;
            Describe = $"MySQL {host}:{port}/{name} (from CS2-SimpleAdmin.json)";
            return;
        }

        if (type.Contains("mysql", StringComparison.OrdinalIgnoreCase))
            _log.LogWarning("Menu styles: CS2-SimpleAdmin uses MySQL but host / user / database is empty, using SQLite");

        PrepareSqliteNative();
        SQLitePCL.Batteries.Init();
        string file = Path.Combine(_configsDir, "PMM_SimpleAdmin", "pmm_simpleadmin.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        _connection = new SqliteConnectionStringBuilder { DataSource = file }.ToString();
        _mysql = false;
        Describe = $"SQLite {file}";
    }

    private static string Str(JsonElement o, string name, string fallback)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private async Task<DbConnection> Open()
    {
        DbConnection conn = _mysql ? new MySqlConnection(_connection) : new SqliteConnection(_connection);
        await conn.OpenAsync();
        return conn;
    }

    private static void Add(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    // ---------------- queries ----------------

    private async Task Exec(string sql)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await Open();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _lock.Release(); }
    }

    private async Task Upsert(ulong steamId, string style)
    {
        await _lock.WaitAsync();
        try
        {
            await using var conn = await Open();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = _mysql
                ? $"INSERT INTO {Table} (steamid, style, updated_at) VALUES (@id, @style, @at) ON DUPLICATE KEY UPDATE style = VALUES(style), updated_at = VALUES(updated_at)"
                : $"INSERT INTO {Table} (steamid, style, updated_at) VALUES (@id, @style, @at) ON CONFLICT(steamid) DO UPDATE SET style = excluded.style, updated_at = excluded.updated_at";
            Add(cmd, "@id", (long)steamId);
            Add(cmd, "@style", style);
            Add(cmd, "@at", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await cmd.ExecuteNonQueryAsync();
        }
        finally { _lock.Release(); }
    }

    private async Task<Dictionary<ulong, string>> LoadAll()
    {
        var result = new Dictionary<ulong, string>();
        await _lock.WaitAsync();
        try
        {
            await using var conn = await Open();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT steamid, style FROM {Table}";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                long id = Convert.ToInt64(reader.GetValue(0));
                string style = Convert.ToString(reader.GetValue(1)) ?? "";
                if (id > 0 && style != "") result[(ulong)id] = style;
            }
        }
        finally { _lock.Release(); }
        return result;
    }

    // players.json of the first test build: moved into the database once, then renamed.
    private async Task ImportJson()
    {
        string path = Path.Combine(_configsDir, "PMM_SimpleAdmin", "players.json");
        if (!File.Exists(path)) return;
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(path));
        int n = 0;
        if (map != null)
            foreach (var (id, style) in map)
                if (ulong.TryParse(id, out ulong steamId) && !string.IsNullOrEmpty(style))
                {
                    await Upsert(steamId, style);
                    n++;
                }
        File.Move(path, path + ".imported", true);
        _log.LogInformation("Menu styles: {Count} players moved from players.json", n);
    }

    // ---------------- SQLite native (same as PMM core DataBaseService) ----------------

    private void PrepareSqliteNative()
    {
        string nativePath = ResolveSqliteNativePath();
        if (!File.Exists(nativePath))
        {
            _log.LogError("SQLite native library was not found. Expected {Path}", nativePath);
            return;
        }

        Assembly provider = typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly;
        try
        {
            NativeLibrary.SetDllImportResolver(provider, (libraryName, _, _) =>
                libraryName == "e_sqlite3" ? NativeLibrary.Load(nativePath) : IntPtr.Zero);
        }
        catch (InvalidOperationException)
        {
            // already set (hot reload)
        }
    }

    private string ResolveSqliteNativePath()
    {
        string rid, fileName;
        if (OperatingSystem.IsWindows())
        {
            rid = "win-x64";
            fileName = "e_sqlite3.dll";
        }
        else if (File.Exists("/lib/ld-musl-x86_64.so.1"))
        {
            rid = "linux-musl-x64";
            fileName = "libe_sqlite3.so";
        }
        else
        {
            rid = "linux-x64";
            fileName = "libe_sqlite3.so";
        }

        string runtimePath = Path.Combine(_moduleDir, "runtimes", rid, "native", fileName);
        if (File.Exists(runtimePath)) return runtimePath;
        string beside = Path.Combine(_moduleDir, fileName);
        return File.Exists(beside) ? beside : runtimePath;
    }
}
