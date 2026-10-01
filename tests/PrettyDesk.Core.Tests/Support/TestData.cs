using PrettyDesk.Core.Abstractions;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Detection;
using PrettyDesk.Core.Settings;

namespace PrettyDesk.Core.Tests.Support;

internal static class TestData
{
    public static GameEntry Game(string id, string packId = "", Action<DetectionRulesBuilder>? configure = null)
    {
        var builder = new DetectionRulesBuilder();
        configure?.Invoke(builder);
        return new GameEntry { Id = id, DisplayName = id, PackId = string.IsNullOrEmpty(packId) ? "game." + id : packId, Detection = builder.Build() };
    }

    public static GameRegistry Registry(IEnumerable<GameEntry> games, IEnumerable<string>? excludes = null, AppSettings? settings = null) =>
        GameRegistry.Build(new CatalogDocument { Games = games.ToList(), ExcludeExeNames = (excludes ?? []).ToList() }, settings ?? new AppSettings());

    public static ProcessInfo Proc(int pid, string exe) => new(pid, exe, 1);

    public sealed class DetectionRulesBuilder
    {
        public List<string> Exe { get; } = [];
        public List<uint> Steam { get; } = [];
        public List<string> Path { get; } = [];
        public List<string> Title { get; } = [];
        public List<string> Exclude { get; } = [];

        public DetectionRules Build() => new()
        {
            ExeNames = Exe,
            SteamAppIds = Steam,
            PathContains = Path,
            WindowTitleContains = Title,
            ExcludeExeNames = Exclude,
        };
    }

    public sealed class FakeDetails : IProcessDetailsSource
    {
        public Dictionary<int, string?> Paths { get; } = [];
        public Dictionary<int, string?> Titles { get; } = [];
        public int PathCalls { get; private set; }
        public int TitleCalls { get; private set; }

        public string? TryGetImagePath(int pid)
        {
            PathCalls++;
            return Paths.GetValueOrDefault(pid);
        }

        public string? TryGetMainWindowTitle(int pid)
        {
            TitleCalls++;
            return Titles.GetValueOrDefault(pid);
        }
    }
}
