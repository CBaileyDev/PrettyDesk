using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace PrettyDesk.Core.Settings;

/// <summary>A pure <c>vN → vN+1</c> migration operating on the raw JSON so unknown fields survive (SPEC §5.7).</summary>
public interface ISettingsMigration
{
    int FromVersion { get; }

    void Migrate(JsonObject document);
}

public sealed record JsonLoadResult<T>(T Value, bool RecoveredFromCorruption, string? CorruptBackupPath);

/// <summary>Validates collection elements and other invariants not enforced by the JSON serializer.</summary>
public interface IJsonFileValidatable
{
    bool IsValid();
}

/// <summary>
/// Versioned JSON file with atomic writes (temp + replace) and corrupt-file recovery: a file that cannot be read is
/// moved to <c>name.corrupt-&lt;timestamp&gt;.json</c> and replaced with defaults.
/// </summary>
public sealed class JsonFileStore<T>
    where T : class, new()
{
    private readonly string _path;
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly int _currentVersion;
    private readonly Dictionary<int, ISettingsMigration> _migrations;
    private readonly TimeProvider _time;
    private bool _unreadableOnLoad;

    public JsonFileStore(
        string path,
        JsonTypeInfo<T> typeInfo,
        int currentVersion,
        TimeProvider time,
        IEnumerable<ISettingsMigration>? migrations = null)
    {
        _path = path;
        _typeInfo = typeInfo;
        _currentVersion = currentVersion;
        _time = time;
        _migrations = (migrations ?? []).ToDictionary(m => m.FromVersion);
    }

    public string Path => _path;

    public JsonLoadResult<T> Load()
    {
        if (!File.Exists(_path))
        {
            return new JsonLoadResult<T>(new T(), false, null);
        }

        try
        {
            var text = File.ReadAllText(_path);
            if (JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) is not JsonObject document)
            {
                return Recover();
            }

            // A hand-edited file without schemaVersion is current unless a real v0 migration exists.
            var version = document["schemaVersion"]?.GetValue<int>() ?? (_migrations.ContainsKey(0) ? 0 : _currentVersion);
            while (version < _currentVersion)
            {
                if (!_migrations.TryGetValue(version, out var migration))
                {
                    return Recover();
                }

                migration.Migrate(document);
                version++;
                document["schemaVersion"] = version;
            }

            var value = document.Deserialize(_typeInfo);
            return value is null || value is IJsonFileValidatable { } validatable && !validatable.IsValid()
                ? Recover() : new JsonLoadResult<T>(value, false, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked by antivirus or a backup tool, not corrupt: keep the file where it is and never overwrite it blindly.
            _unreadableOnLoad = true;
            return new JsonLoadResult<T>(new T(), false, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException)
        {
            return Recover();
        }
    }

    public void Save(T value)
    {
        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (_unreadableOnLoad)
        {
            // The file could not be read at startup, so defaults are in memory. Move the real file aside before replacing
            // it; if that fails too, throw so the caller retries later rather than destroying the user's settings.
            var stamp = _time.GetUtcNow().ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
            var name = System.IO.Path.GetFileNameWithoutExtension(_path);
            if (File.Exists(_path))
            {
                File.Move(_path, System.IO.Path.Combine(directory ?? ".", $"{name}.unreadable-{stamp}.json"), overwrite: true);
            }

            _unreadableOnLoad = false;
        }

        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(value, _typeInfo));
        File.Move(temp, _path, overwrite: true);
    }

    private JsonLoadResult<T> Recover()
    {
        string? backup = null;
        try
        {
            var stamp = _time.GetUtcNow().ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
            var directory = System.IO.Path.GetDirectoryName(_path) ?? ".";
            var name = System.IO.Path.GetFileNameWithoutExtension(_path);
            backup = System.IO.Path.Combine(directory, $"{name}.corrupt-{stamp}.json");
            File.Move(_path, backup, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            backup = null;
        }

        return new JsonLoadResult<T>(new T(), true, backup);
    }
}
