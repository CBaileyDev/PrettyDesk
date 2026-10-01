using PrettyDesk.Core.Privacy;
using Serilog;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace PrettyDesk.App;

/// <summary>Rolling file log: 7 days, 10 MB cap per file (SPEC §5.1), scrubbed of the user name and profile path (SPEC §10).</summary>
public static class Logging
{
    private const string Template = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static ILogger Create(AppPaths paths)
    {
        var formatter = new ScrubbingFormatter(new MessageTemplateTextFormatter(Template), paths.LocalAppData);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                formatter,
                Path.Combine(paths.Logs, "prettydesk-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10_000_000,
                rollOnFileSizeLimit: true,
                shared: true)
            .CreateLogger();
    }

    private sealed class ScrubbingFormatter : ITextFormatter
    {
        private readonly ITextFormatter _inner;
        private readonly string _localAppData;
        private readonly string _userName = Environment.UserName;
        private readonly string _profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public ScrubbingFormatter(ITextFormatter inner, string localAppData)
        {
            _inner = inner;
            _localAppData = localAppData;
        }

        public void Format(LogEvent logEvent, TextWriter output)
        {
            using var buffer = new StringWriter();
            _inner.Format(logEvent, buffer);
            output.Write(LogScrubber.Scrub(buffer.ToString(), _userName, _profile, _localAppData));
        }
    }
}
