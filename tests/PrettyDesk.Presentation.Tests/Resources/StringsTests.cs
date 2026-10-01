using System.Xml.Linq;
using PrettyDesk.Presentation.Resources;
using Shouldly;
using Xunit;

namespace PrettyDesk.Presentation.Tests.Resources;

public class StringsTests
{
    private static string ResxPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrettyDesk.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "src", "PrettyDesk.Presentation", "Resources", "Strings.resx");
    }

    [Fact]
    public void Every_resx_key_has_a_typed_accessor_and_resolves_to_real_text()
    {
        var keys = XDocument.Load(ResxPath()).Root!.Elements("data").Select(e => (string)e.Attribute("name")!).ToList();

        Strings.AllKeys.ShouldBe(keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        foreach (var key in keys)
        {
            Strings.Get(key).ShouldNotBe(key, $"{key} is missing from the compiled resource");
        }
    }

    [Fact]
    public void Keys_follow_the_Area_Name_convention()
    {
        foreach (var key in Strings.AllKeys)
        {
            System.Text.RegularExpressions.Regex.IsMatch(key, @"^[A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+$").ShouldBeTrue(key);
        }
    }

    [Fact]
    public void Format_placeholders_in_every_string_are_well_formed()
    {
        foreach (var key in Strings.AllKeys)
        {
            var text = Strings.Get(key);
            var placeholders = System.Text.RegularExpressions.Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).Distinct().Count();

            Should.NotThrow(() => string.Format(System.Globalization.CultureInfo.InvariantCulture, text, Enumerable.Repeat((object)"x", Math.Max(placeholders, 1) + 3).ToArray()), key);
        }
    }

    [Fact]
    public void Missing_keys_fall_back_to_the_key_name_so_gaps_are_visible_not_blank() =>
        Strings.Get("Nope_Missing").ShouldBe("Nope_Missing");
}
