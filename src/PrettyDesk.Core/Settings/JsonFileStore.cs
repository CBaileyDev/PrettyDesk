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

            var version = document["schemaVersion"]?.GetValue<int>() ?? 0;
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
            return value is null ? Recover() : new JsonLoadResult<T>(value, false, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
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
