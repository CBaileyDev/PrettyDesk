using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PrettyDesk.Presentation.Resources;
using PrettyDesk.Presentation.ViewModels;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.Xaml;

/// <summary>
/// XAML cannot be run on Linux, and a mistyped binding only shows up as a silent runtime failure. This static check reads every
/// view and verifies, against the real view-model types, that each <c>{Binding}</c> path resolves, each <c>x:Static</c> string
/// exists in <see cref="Strings"/>, and each <c>StaticResource</c> key is defined. The Windows UI smoke test then covers the
/// runtime side.
/// </summary>
public partial class XamlBindingTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace D = "http://schemas.microsoft.com/expression/blend/2008";
    private static readonly string AppDirectory = Path.Combine(FindRoot(), "src", "PrettyDesk.App");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrettyDesk.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    [GeneratedRegex(@"\{Binding(?<body>(?:[^{}]|\{[^{}]*\})*)\}")]
    private static partial Regex BindingExpression();

    [GeneratedRegex(@"\{x:Static\s+(?<type>\w+):(?<member>[\w\.]+)\}")]
    private static partial Regex StaticExpression();

    [GeneratedRegex(@"\{(?:StaticResource|DynamicResource)\s+(?<key>[\w\.]+)\}")]
    private static partial Regex ResourceExpression();

    [GeneratedRegex(@"\{x:Type\s+(?:\w+:)?(?<type>\w+)\}")]
    private static partial Regex TypeExpression();

    public static IEnumerable<object[]> Views() =>
        Directory.GetFiles(Path.Combine(AppDirectory, "Views"), "*.xaml").Select(f => new object[] { Path.GetFileName(f) });

    private static Type? ViewModelType(string name) =>
        typeof(ViewModelBase).Assembly.GetTypes().FirstOrDefault(t => t.Name == name && t.IsPublic);

    private static HashSet<string> DefinedResourceKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { Path.Combine(AppDirectory, "Resources", "Styles.xaml"), Path.Combine(AppDirectory, "App.xaml") })
        {
            foreach (var key in XDocument.Load(file).Descendants().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null))
            {
                keys.Add(key!);
            }
        }

        return keys;
    }

    [Theory]
    [MemberData(nameof(Views))]
    public void Every_binding_static_and_resource_in_the_view_resolves(string fileName)
    {
        var document = XDocument.Load(Path.Combine(AppDirectory, "Views", fileName));
        var root = document.Root!;
        var problems = new List<string>();
        var globalKeys = DefinedResourceKeys();
        var localKeys = root.DescendantsAndSelf().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null).Select(k => k!).ToHashSet();

        var design = (string?)root.Attribute(D + "DataContext");
        var rootType = design is null ? null : ResolveDesignType(design);
        if (design is not null && rootType is null)
        {
            problems.Add($"d:DataContext '{design}' does not name a view-model type");
        }

        Walk(root, rootType, rootType, problems, globalKeys, localKeys);

        problems.ShouldBeEmpty($"{fileName}:\n" + string.Join("\n", problems));
    }

    private static Type? ServiceType(string name) =>
        typeof(PrettyDesk.Presentation.Services.RunningApp).Assembly.GetTypes().FirstOrDefault(t => t.Name == name && t.IsPublic);

    private static Type? ResolveDesignType(string design)
    {
        var match = Regex.Match(design, @"(?:Type=)?(?:\w+:)?(?<name>\w+ViewModel|\w+State)\b");
        return match.Success ? ViewModelType(match.Groups["name"].Value) : null;
    }

    private static void Walk(XElement element, Type? current, Type? viewRoot, List<string> problems, HashSet<string> globalKeys, HashSet<string> localKeys)
    {
        var context = current;
        var local = element.Name.LocalName;

        if (local == "DataTemplate" && element.Name.NamespaceName.Contains("presentation", StringComparison.Ordinal))
        {
            var dataType = (string?)element.Attribute("DataType");
            if (dataType is null)
            {
                problems.Add("A DataTemplate has no DataType, so its bindings cannot be verified");
            }
            else if (TypeExpression().Match(dataType) is { Success: true } m)
            {
                context = ViewModelType(m.Groups["type"].Value) ?? ClrTypeOrNull(m.Groups["type"].Value) ?? ServiceType(m.Groups["type"].Value);
                if (context is null)
                {
                    problems.Add($"DataTemplate DataType '{dataType}' is not a known type");
                }
            }
        }

        // Design-time annotation for styles/templates outside a DataTemplate (e.g. ItemContainerStyle setters).
        if ((string?)element.Attribute(D + "DataType") is { } designType && TypeExpression().Match(designType) is { Success: true } dm)
        {
            context = ViewModelType(dm.Groups["type"].Value) ?? ClrTypeOrNull(dm.Groups["type"].Value);
        }

        foreach (var attribute in element.Attributes())
        {
            var value = attribute.Value;
            if (attribute.Name.LocalName == "DataType" || value.Length == 0 || value[0] != '{')
            {
                CheckEmbedded(value, attribute, current, viewRoot, problems, globalKeys, localKeys);
                continue;
            }

            CheckEmbedded(value, attribute, context, viewRoot, problems, globalKeys, localKeys);

            if (attribute.Name.LocalName == "DataContext" && BindingExpression().Match(value) is { Success: true } binding)
            {
                var path = PathOf(binding.Groups["body"].Value);
                var resolved = context is null || path is null ? null : ResolvePath(context, path, out _);
                if (resolved is not null)
                {
                    context = resolved;
                }
            }
        }

        foreach (var child in element.Elements())
        {
            // Property elements (e.g. <Border.Background>) keep the parent's data context.
            Walk(child, context, viewRoot, problems, globalKeys, localKeys);
        }
    }

    private static void CheckEmbedded(string value, XAttribute attribute, Type? context, Type? viewRoot, List<string> problems, HashSet<string> globalKeys, HashSet<string> localKeys)
    {
        var where = $"{attribute.Parent!.Name.LocalName}.{attribute.Name.LocalName}";

        foreach (Match match in BindingExpression().Matches(value))
        {
            var body = match.Groups["body"].Value;
            var path = PathOf(body);
            if (path is null || body.Contains("ElementName", StringComparison.Ordinal))
            {
                continue;
            }

            var target = context;
            var effective = path;
            if (body.Contains("RelativeSource", StringComparison.Ordinal))
            {
                // Supported pattern: {Binding DataContext.X, RelativeSource={RelativeSource AncestorType=UserControl}} → the view's root VM.
                if (!path.StartsWith("DataContext.", StringComparison.Ordinal))
                {
                    continue;
                }

                target = viewRoot;
                effective = path["DataContext.".Length..];
            }

            if (target is null)
            {
                problems.Add($"{where}: binding '{path}' has no known data context (add d:DataContext or a DataType)");
                continue;
            }

            if (ResolvePath(target, effective, out var failure) is null && failure is not null)
            {
                problems.Add($"{where}: binding '{path}' failed on {target.Name}: {failure}");
            }
        }

        foreach (Match match in StaticExpression().Matches(value))
        {
            var typeName = match.Groups["type"].Value;
            var member = match.Groups["member"].Value;
            if (typeName == "res")
            {
                var name = member.Split('.')[^1];
                typeof(Strings).GetProperty(name, BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull($"{where}: Strings.{name} does not exist");
            }
            else if (typeName == "vm")
            {
                var parts = member.Split('.');
                var type = ViewModelType(parts[0]);
                (type?.GetMember(parts[^1], BindingFlags.Public | BindingFlags.Static).Length > 0).ShouldBeTrue($"{where}: {member} does not exist");
            }
        }

        foreach (Match match in ResourceExpression().Matches(value))
        {
            var key = match.Groups["key"].Value;
            var dynamicKnown = key.EndsWith("Brush", StringComparison.Ordinal) || key.EndsWith("Color", StringComparison.Ordinal);
            if (!globalKeys.Contains(key) && !localKeys.Contains(key) && !dynamicKnown)
            {
                problems.Add($"{where}: resource '{key}' is not defined");
            }
        }
    }

    private static string? PathOf(string body)
    {
        var tokens = SplitTopLevel(body);
        foreach (var token in tokens)
        {
            var trimmed = token.Trim();
            if (trimmed.StartsWith("Path=", StringComparison.Ordinal))
            {
                return trimmed["Path=".Length..].Trim();
            }
        }

        var first = tokens.Count > 0 ? tokens[0].Trim() : string.Empty;
        return first.Length == 0 || first.Contains('=', StringComparison.Ordinal) || first == "." ? null : first;
    }

    private static List<string> SplitTopLevel(string body)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '{')
            {
                depth++;
            }
            else if (body[i] == '}')
            {
                depth--;
            }
            else if (body[i] == ',' && depth == 0)
            {
                result.Add(body[start..i]);
                start = i + 1;
            }
        }

        result.Add(body[start..]);
        return result;
    }

    /// <summary>Resolves a dotted property path. Returns the final type, or null when it could not be followed (failure explains why).</summary>
    private static Type? ResolvePath(Type start, string path, out string? failure)
    {
        failure = null;
        var type = start;
        foreach (var raw in path.Split('.'))
        {
            var name = Regex.Replace(raw, @"\[.*\]", string.Empty);
            if (name.Length == 0)
            {
                return null;
            }

            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null)
            {
                failure = $"'{name}' is not a public property of {type.Name}";
                return null;
            }

            type = property.PropertyType;
            if (type.IsPrimitive || type == typeof(string) || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                return null; // the rest cannot be verified against our own types
            }
        }

        return type;
    }

    private static Type? ClrTypeOrNull(string name) =>
        name == "String" ? typeof(string) : typeof(ViewModelBase).Assembly.GetTypes().FirstOrDefault(t => t.Name == name);

    [Fact]
    public void Every_xaml_view_declares_its_design_time_view_model_when_it_binds()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(AppDirectory, "Views"), "*.xaml"))
        {
            var text = File.ReadAllText(file);
            if (text.Contains("{Binding", StringComparison.Ordinal))
            {
                text.ShouldContain("d:DataContext", Case.Sensitive, $"{Path.GetFileName(file)} binds but does not declare d:DataContext");
            }
        }
    }

    [Fact]
    public void The_checker_actually_catches_a_mistyped_binding()
    {
        ResolvePath(typeof(HomeViewModel), "Headline", out var ok);
        ok.ShouldBeNull();

        ResolvePath(typeof(HomeViewModel), "Headlin", out var bad);
        bad.ShouldNotBeNull();

        ResolvePath(typeof(HomeViewModel), "Monitors.Count", out var chained);
        chained.ShouldBeNull();
    }
}
