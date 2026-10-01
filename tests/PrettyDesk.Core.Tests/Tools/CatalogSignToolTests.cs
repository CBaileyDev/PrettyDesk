using System.Text;
using PrettyDesk.Core.Tests.Support;
using PrettyDesk.Tools.CatalogSign;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Tools;

public sealed class CatalogSignToolTests : IDisposable
{
    private const string Catalog = """{ "schemaVersion": 1, "catalogVersion": "1", "minAppVersion": "1.0.0", "contentBaseUrl": "https://c.example/v1/", "games": [], "packs": [] }""";
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = CatalogSignCommands.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private string KeyGen(out string publicKey)
    {
        var keyPath = _dir.File("signing.key");
        var result = Run("keygen", "--out", keyPath);
        result.Code.ShouldBe(0, result.Err);
        publicKey = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
        return keyPath;
    }

    [Fact]
    public void Keygen_sign_verify_round_trip()
    {
        var key = KeyGen(out var pub);
        var catalog = _dir.File("catalog.json");
        File.WriteAllText(catalog, Catalog);

        Run("sign", catalog, "--key", key).Code.ShouldBe(0);
        var verify = Run("verify", catalog, "--pub", pub);

        verify.Code.ShouldBe(0);
        verify.Out.ShouldContain("OK");
    }

    [Fact]
    public void Verify_fails_after_the_catalog_is_modified()
    {
        var key = KeyGen(out var pub);
        var catalog = _dir.File("catalog.json");
        File.WriteAllText(catalog, Catalog);
        Run("sign", catalog, "--key", key);

        File.WriteAllText(catalog, Catalog.Replace("\"1\"", "\"2\"", StringComparison.Ordinal));

        Run("verify", catalog, "--pub", pub).Code.ShouldBe(1);
    }

    [Fact]
    public void Keygen_never_overwrites_an_existing_private_key()
    {
        var key = KeyGen(out _);
        var before = File.ReadAllText(key);

        Run("keygen", "--out", key).Code.ShouldBe(1);

        File.ReadAllText(key).ShouldBe(before);
    }

    [Fact]
    public void Sign_refuses_an_invalid_catalog()
    {
        var key = KeyGen(out _);
        var catalog = _dir.File("catalog.json");
        File.WriteAllText(catalog, """{ "schemaVersion": 1, "contentBaseUrl": "http://insecure/" }""");

        var result = Run("sign", catalog, "--key", key);

        result.Code.ShouldBe(1);
        File.Exists(catalog + ".sig").ShouldBeFalse();
    }

    [Fact]
    public void Sign_can_read_the_key_from_an_environment_variable()
    {
        var key = KeyGen(out var pub);
        Environment.SetEnvironmentVariable("PRETTYDESK_TEST_KEY", File.ReadAllText(key));
        try
        {
            var catalog = _dir.File("catalog.json");
            File.WriteAllText(catalog, Catalog);

            Run("sign", catalog, "--key-env", "PRETTYDESK_TEST_KEY").Code.ShouldBe(0);
            Run("verify", catalog, "--pub", pub).Code.ShouldBe(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PRETTYDESK_TEST_KEY", null);
        }
    }

    [Fact]
    public void Usage_errors_return_non_zero_with_help()
    {
        Run().Code.ShouldBe(2);
        Run("frobnicate").Err.ShouldContain("Unknown command");
        Run("sign").Code.ShouldBe(1);
        Run("verify").Code.ShouldBe(1);
        Run("sign", "missing.json", "--key", "nope").Code.ShouldBe(1);
        Encoding.UTF8.GetBytes(CatalogSignCommands.Usage).Length.ShouldBeGreaterThan(0);
    }
}
