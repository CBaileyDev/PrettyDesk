using System.Text.Json;
using PrettyDesk.Core.Abstractions;

namespace PrettyDesk.Core.Settings;

/// <summary>File-backed <see cref="ISettingsProvider"/>. Snapshots are cloned before mutation so readers never see torn state.</summary>
public sealed class SettingsService : ISettingsProvider
{
    private readonly JsonFileStore<AppSettings> _store;
    private readonly object _gate = new();
    private AppSettings _current;

    public SettingsService(JsonFileStore<AppSettings> store)
    {
        _store = store;
        var loaded = store.Load();
        _current = loaded.Value;
        RecoveredFromCorruption = loaded.RecoveredFromCorruption;
    }

    /// <summary>True when the settings file was unreadable at startup and replaced by defaults (the UI notifies the user once).</summary>
    public bool RecoveredFromCorruption { get; }

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event Action? Changed;

    public void Update(Action<AppSettings> mutate)
    {
        lock (_gate)
        {
            var clone = Clone(_current);
            mutate(clone);
            _current = clone;
            _store.Save(clone);
        }

        Changed?.Invoke();
    }

    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings), SettingsJsonContext.Default.AppSettings)!;
}
