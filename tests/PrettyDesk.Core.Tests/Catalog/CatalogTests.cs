using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PrettyDesk.Core.Catalog;
using PrettyDesk.Core.Tests.Support;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Catalog;

public class CatalogSigningTests
{
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("""{"schemaVersion":1}""");

    [Fact]
    public void Valid_signature_verifies()
    {
        var (priv, pub) = CatalogSigning.GenerateKeyPair();
        var signature = CatalogSigning.Sign(Data, priv);

        new CatalogSignatureVerifier([pub]).Verify(Data, signature).ShouldBeTrue();
    }

    [Fact]
    public void Tampered_data_fails()
    {
        var (priv, pub) = CatalogSigning.GenerateKeyPair();
        var signature = CatalogSigning.Sign(Data, priv);
        var tampered = Encoding.UTF8.GetBytes("""{"schemaVersion":2}""");

        new CatalogSignatureVerifier([pub]).Verify(tampered, signature).ShouldBeFalse();
    }

    [Fact]
    public void Signature_from_another_key_fails()
    {
        var (priv, _) = CatalogSigning.GenerateKeyPair();
        var (_, otherPub) = CatalogSigning.GenerateKeyPair();

        new CatalogSignatureVerifier([otherPub]).Verify(Data, CatalogSigning.Sign(Data, priv)).ShouldBeFalse();
    }

    [Fact]
    public void Any_trusted_key_may_match_for_key_rotation()
    {
        var (oldPriv, oldPub) = CatalogSigning.GenerateKeyPair();
        var (_, newPub) = CatalogSigning.GenerateKeyPair();

        new CatalogSignatureVerifier([newPub, oldPub]).Verify(Data, CatalogSigning.Sign(Data, oldPriv)).ShouldBeTrue();
    }

    [Fact]
    public void Truncated_or_garbage_signatures_fail_without_throwing()
    {
        var (_, pub) = CatalogSigning.GenerateKeyPair();
        var verifier = new CatalogSignatureVerifier([pub]);

        verifier.Verify(Data, new byte[10]).ShouldBeFalse();
        verifier.Verify(Data, new byte[64]).ShouldBeFalse();
        verifier.VerifyBase64(Data, "!!!not base64!!!").ShouldBeFalse();
    }

    [Fact]
    public void Malformed_trusted_key_does_not_block_the_valid_one()
    {
        var (priv, pub) = CatalogSigning.GenerateKeyPair();

        new CatalogSignatureVerifier([[1, 2, 3], pub]).Verify(Data, CatalogSigning.Sign(Data, priv)).ShouldBeTrue();
    }

    [Fact]
    public void No_keys_means_nothing_verifies()
    {
        var (priv, _) = CatalogSigning.GenerateKeyPair();
        var verifier = new CatalogSignatureVerifier([]);

        verifier.HasKeys.ShouldBeFalse();
        verifier.Verify(Data, CatalogSigning.Sign(Data, priv)).ShouldBeFalse();
    }

    [Fact]
    public void Shipped_trust_list_is_valid_base64()
    {
        Should.NotThrow(() => TrustedKeys.CreateVerifier());
    }
}

public class CatalogValidatorTests
{
    private static readonly Version App = new(1, 0, 0);

    private static string Json(Action<Dictionary<string, object?>>? edit = null)
    {
        var doc = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["catalogVersion"] = "2026.10.01.1",
            ["minAppVersion"] = "1.0.0",
            ["contentBaseUrl"] = "https://content.example.com/v1/",
            ["games"] = new[] { new { id = "cs2", displayName = "CS2", packId = "game.cs2", detection = new { exeNames = new[] { "cs2.exe" }, steamAppIds = new[] { 730 } } } },
            ["packs"] = new[]
            {
                new
                {
                    id = "game.cs2", kind = "game", version = 1, title = "CS2",
                    wallpapers = new[]
                    {
                        new
                        {
                            id = "cs2.hero-01", title = "T", tone = "dark", focal = new { x = 0.6, y = 0.4 }, futureField = 5,
                            variants = new Dictionary<string, object> { ["16x9"] = new { path = "packs/game.cs2/v1/a.jpg", w = 3840, h = 2160, bytes = 10, sha256 = new string('a', 64) } },
                        },
                    },
                },
            },
            ["collections"] = new[] { new { id = "default.x", packId = "game.cs2", title = "X" } },
            ["someFutureTopLevelField"] = new { a = 1 },
        };
        edit?.Invoke(doc);
        return JsonSerializer.Serialize(doc);
    }

    private static CatalogCheck Check(string json) => CatalogValidator.ParseAndValidate(Encoding.UTF8.GetBytes(json), App);

    [Fact]
    public void Valid_catalog_parses_and_unknown_fields_are_ignored()
    {
        var check = Check(Json());

        check.Ok.ShouldBeTrue(check.Detail);
        check.Document!.Games.ShouldHaveSingleItem().Detection.SteamAppIds.ShouldBe([730u]);
        check.Document.FindWallpaper("cs2.hero-01")!.Variants["16x9"].W.ShouldBe(3840);
        check.Document.FindWallpaper("cs2.hero-01")!.Focal.X.ShouldBe(0.6);
    }

    [Fact]
    public void Malformed_json_is_rejected() => Check("{ nope").Rejection.ShouldBe(CatalogRejection.Malformed);

    [Theory]
    [InlineData("packs", "null")]
    [InlineData("games", "[null]")]
    [InlineData("wallpapers", "[null]")]
    [InlineData("detection", "null")]
    [InlineData("exeNames", "[null]")]
    [InlineData("variants", "{\"16x9\":null}")]
    [InlineData("focal", "null")]
    public void Explicit_nulls_are_rejected_without_throwing(string name, string replacement)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Json())!;
        var parent = name switch
        {
            "packs" or "games" => node,
            "wallpapers" => node["packs"]![0]!,
            "detection" => node["games"]![0]!,
            "exeNames" => node["games"]![0]!["detection"]!,
            _ => node["packs"]![0]!["wallpapers"]![0]!,
        };
        parent[name] = System.Text.Json.Nodes.JsonNode.Parse(replacement);

        Check(node.ToJsonString()).Ok.ShouldBeFalse();
    }

    [Fact]
    public void Newer_schema_is_rejected() =>
        Check(Json(d => d["schemaVersion"] = 2)).Rejection.ShouldBe(CatalogRejection.UnsupportedSchema);

    [Fact]
    public void Catalog_requiring_a_newer_app_is_not_used() =>
        Check(Json(d => d["minAppVersion"] = "1.1.0")).Rejection.ShouldBe(CatalogRejection.RequiresNewerApp);

    [Fact]
    public void Catalog_for_the_current_app_version_is_used() =>
        Check(Json(d => d["minAppVersion"] = "1.0.0")).Ok.ShouldBeTrue();

    [Theory]
    [InlineData("http://content.example.com/v1/")]
    [InlineData("https://content.example.com/v1")]
    [InlineData("ftp://x/")]
    [InlineData("not a url")]
    public void Base_url_must_be_https_and_end_with_a_slash(string url) =>
        Check(Json(d => d["contentBaseUrl"] = url)).Rejection.ShouldBe(CatalogRejection.Invalid);

    [Theory]
    [InlineData("../evil.jpg")]
    [InlineData("/abs/evil.jpg")]
    [InlineData("https://evil.example/x.jpg")]
    [InlineData("a\\b.jpg")]
    public void File_paths_must_be_relative_and_safe(string path)
    {
        var json = Json().Replace("packs/game.cs2/v1/a.jpg", path.Replace("\\", "\\\\"), StringComparison.Ordinal);

        Check(json).Rejection.ShouldBe(CatalogRejection.Invalid);
    }

    [Fact]
    public void Game_referencing_a_missing_pack_is_rejected() =>
        Check(Json().Replace("\"packId\":\"game.cs2\",\"detection\"", "\"packId\":\"game.nope\",\"detection\"", StringComparison.Ordinal)).Rejection.ShouldBe(CatalogRejection.Invalid);

    [Fact]
    public void Duplicate_wallpaper_ids_are_rejected()
    {
        var json = Json();
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var packs = doc.GetProperty("packs")[0];
        var wallpapers = packs.GetProperty("wallpapers")[0].GetRawText();
        var duplicated = json.Replace("\"wallpapers\":[" + wallpapers + "]", "\"wallpapers\":[" + wallpapers + "," + wallpapers + "]", StringComparison.Ordinal);

        Check(duplicated).Rejection.ShouldBe(CatalogRejection.Invalid);
    }

    [Fact]
    public void Unknown_variant_key_is_rejected() =>
        Check(Json().Replace("\"16x9\"", "\"7x7\"", StringComparison.Ordinal)).Rejection.ShouldBe(CatalogRejection.Invalid);

    [Fact]
    public void Bad_sha256_is_rejected() =>
        Check(Json().Replace(new string('a', 64), "XYZ", StringComparison.Ordinal)).Rejection.ShouldBe(CatalogRejection.Invalid);

    [Fact]
    public void Focal_out_of_range_is_rejected() =>
        Check(Json().Replace("\"x\":0.6", "\"x\":1.6", StringComparison.Ordinal)).Rejection.ShouldBe(CatalogRejection.Invalid);

    [Fact]
    public void Game_without_a_positive_rule_is_rejected() =>
        Check(Json().Replace("\"exeNames\":[\"cs2.exe\"],\"steamAppIds\":[730]", "\"exeNames\":[]", StringComparison.Ordinal)).Rejection.ShouldBe(CatalogRejection.Invalid);

    [Theory]
    [InlineData("2026.10.01.1", "2026.10.01.1", 0)]
    [InlineData("2026.10.01.2", "2026.10.01.1", 1)]
    [InlineData("2026.9.30.9", "2026.10.01.1", -1)]
    [InlineData("2026.10.01", "2026.10.01.0", 0)]
    [InlineData("2026.10.10", "2026.10.9", 1)]
    [InlineData("", "2026.1", -1)]
    public void Catalog_versions_compare_numerically(string a, string b, int expected) =>
        Math.Sign(CatalogVersion.Compare(a, b)).ShouldBe(expected);
}

public sealed class CatalogServiceTests : IDisposable
{
    private const string Base = "https://content.example.com/v1/";
    private readonly TempDir _dir = new();
    private readonly FakeTimeProvider _time = new();
    private readonly (byte[] Priv, byte[] Pub) _keys = CatalogSigning.GenerateKeyPair();

    public void Dispose() => _dir.Dispose();

    private static byte[] CatalogJson(string version, string minApp = "1.0.0", string game = "cs2") =>
        Encoding.UTF8.GetBytes($$"""
        { "schemaVersion": 1, "catalogVersion": "{{version}}", "minAppVersion": "{{minApp}}", "contentBaseUrl": "{{Base}}",
          "excludeExeNames": ["steam.exe"],
          "games": [ { "id": "{{game}}", "displayName": "G", "packId": "p", "detection": { "exeNames": ["{{game}}.exe"] } } ],
          "packs": [ { "id": "p", "kind": "game", "version": 1, "title": "P", "wallpapers": [] } ] }
        """);

    private string Sign(byte[] data) => Convert.ToBase64String(CatalogSigning.Sign(data, _keys.Priv));

    private CatalogService Create(FakeHttpHandler handler, string? bundledVersion = "2026.10.01.1", bool withKey = true)
    {
        var bundledPath = _dir.File("bundled.json");
        if (bundledVersion is not null)
        {
            File.WriteAllBytes(bundledPath, CatalogJson(bundledVersion));
        }

        var options = new CatalogServiceOptions(bundledPath, _dir.File("cache"), Base, new Version(1, 0, 0));
        var service = new CatalogService(options, new HttpClient(handler), new CatalogSignatureVerifier(withKey ? [_keys.Pub] : []), _time, NullLogger<CatalogService>.Instance);
        service.LoadInitial();
        return service;
    }

    private static FakeHttpHandler Serve(byte[] catalog, string signature, string? etag = "\"v1\"") =>
        new(req => req.RequestUri!.AbsolutePath.EndsWith("catalog.json.sig", StringComparison.Ordinal)
            ? FakeHttpHandler.Ok(Encoding.UTF8.GetBytes(signature))
            : FakeHttpHandler.Ok(catalog, etag));

    [Fact]
    public void Bundled_snapshot_is_used_when_nothing_else_exists()
    {
        using var service = Create(Serve([], ""));

        service.Current.CatalogVersion.ShouldBe("2026.10.01.1");
        service.Current.ExcludeExeNames.ShouldBe(["steam.exe"]);
    }

    [Fact]
    public void Missing_bundled_snapshot_yields_an_empty_catalog_not_a_crash()
    {
        using var service = Create(Serve([], ""), bundledVersion: null);

        service.Current.Games.ShouldBeEmpty();
    }

    [Fact]
    public async Task Newer_signed_catalog_is_applied_persisted_and_announced()
    {
        var newer = CatalogJson("2026.10.02.1", game: "newgame");
        using var service = Create(Serve(newer, Sign(newer)));
        var changed = 0;
        service.Changed += () => changed++;

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        service.Current.CatalogVersion.ShouldBe("2026.10.02.1");
        changed.ShouldBe(1);
        File.Exists(_dir.File("cache/catalog.json")).ShouldBeTrue();
        File.Exists(_dir.File("cache/catalog.json.sig")).ShouldBeTrue();
    }

    [Fact]
    public async Task Persisted_catalog_survives_a_restart_and_beats_the_older_bundle()
    {
        var newer = CatalogJson("2026.10.02.1");
        using (var first = Create(Serve(newer, Sign(newer))))
        {
            await first.RefreshAsync(TestContext.Current.CancellationToken);
        }

        using var second = Create(Serve([], ""));

        second.Current.CatalogVersion.ShouldBe("2026.10.02.1");
    }

    [Fact]
    public async Task Bundled_snapshot_wins_when_the_app_update_ships_a_newer_one()
    {
        var cached = CatalogJson("2026.10.02.1");
        using (var first = Create(Serve(cached, Sign(cached))))
        {
            await first.RefreshAsync(TestContext.Current.CancellationToken);
        }

        using var second = Create(Serve([], ""), bundledVersion: "2026.11.01.1");

        second.Current.CatalogVersion.ShouldBe("2026.11.01.1");
    }

    [Fact]
    public async Task Bad_signature_keeps_the_last_good_catalog()
    {
        var evil = CatalogJson("2099.01.01.1", game: "evil");
        var (otherPriv, _) = CatalogSigning.GenerateKeyPair();
        using var service = Create(Serve(evil, Convert.ToBase64String(CatalogSigning.Sign(evil, otherPriv))));

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

        service.Current.CatalogVersion.ShouldBe("2026.10.01.1");
        File.Exists(_dir.File("cache/catalog.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Tampered_body_with_a_valid_signature_for_other_content_is_rejected()
    {
        var good = CatalogJson("2026.10.02.1");
        var tampered = CatalogJson("2026.10.02.1", game: "tampered");
        using var service = Create(Serve(tampered, Sign(good)));

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        service.Current.Games.Single().Id.ShouldBe("cs2");
    }

    [Fact]
    public async Task Rollback_to_an_older_signed_catalog_is_refused()
    {
        var older = CatalogJson("2025.01.01.1");
        using var service = Create(Serve(older, Sign(older)));

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        service.Current.CatalogVersion.ShouldBe("2026.10.01.1");
    }

    [Fact]
    public async Task Catalog_requiring_a_newer_app_is_not_applied()
    {
        var future = CatalogJson("2027.01.01.1", minApp: "9.0.0");
        using var service = Create(Serve(future, Sign(future)));

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_without_compiled_in_keys_is_a_quiet_no_op()
    {
        var handler = Serve(CatalogJson("2030.01.01.1"), "x");
        using var service = Create(handler, withKey: false);

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://insecure.example/v1/")]
    [InlineData("https://content.example/v1")]
    public async Task Builds_without_a_valid_content_url_never_touch_the_network(string baseUrl)
    {
        var handler = new FakeHttpHandler(_ => FakeHttpHandler.Status(HttpStatusCode.OK));
        var options = new CatalogServiceOptions(_dir.File("none.json"), _dir.File("cache"), baseUrl, new Version(1, 0, 0));
        using var service = new CatalogService(options, new HttpClient(handler), new CatalogSignatureVerifier([_keys.Pub]), _time, NullLogger<CatalogService>.Instance);

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Etag_is_sent_back_and_304_changes_nothing()
    {
        var newer = CatalogJson("2026.10.02.1");
        var handler = new FakeHttpHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal))
            {
                return FakeHttpHandler.Ok(Encoding.UTF8.GetBytes(Sign(newer)));
            }

            return req.Headers.IfNoneMatch.Any(t => t.Tag == "\"v7\"") ? FakeHttpHandler.Status(HttpStatusCode.NotModified) : FakeHttpHandler.Ok(newer, "\"v7\"");
        });
        using var service = Create(handler);

        await service.RefreshAsync(TestContext.Current.CancellationToken);
        var changed = 0;
        service.Changed += () => changed++;
        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();

        changed.ShouldBe(0);
        handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("catalog.json", StringComparison.Ordinal)).ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Server_errors_are_swallowed(HttpStatusCode code)
    {
        using var service = Create(new FakeHttpHandler(_ => FakeHttpHandler.Status(code)));

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        service.Current.CatalogVersion.ShouldBe("2026.10.01.1");
    }

    [Fact]
    public async Task Network_exceptions_are_swallowed()
    {
        using var service = Create(new FakeHttpHandler(_ => throw new HttpRequestException("offline")));

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    [Fact]
    public async Task Tampered_cache_file_is_ignored_on_the_next_start()
    {
        var newer = CatalogJson("2026.10.02.1");
        using (var first = Create(Serve(newer, Sign(newer))))
        {
            await first.RefreshAsync(TestContext.Current.CancellationToken);
        }

        await File.WriteAllBytesAsync(_dir.File("cache/catalog.json"), CatalogJson("2099.01.01.1", game: "evil"), TestContext.Current.CancellationToken);

        using var second = Create(Serve([], ""));

        second.Current.CatalogVersion.ShouldBe("2026.10.01.1");
    }

    [Fact]
    public void Periodic_refresh_runs_immediately_and_every_24_hours()
    {
        var count = 0;
        var handler = new FakeHttpHandler(_ =>
        {
            Interlocked.Increment(ref count);
            return FakeHttpHandler.Status(HttpStatusCode.NotFound);
        });
        using var service = Create(handler);

        service.StartPeriodicRefresh();
        SpinUntil(() => Volatile.Read(ref count) >= 1);
        _time.Advance(TimeSpan.FromHours(24));
        SpinUntil(() => Volatile.Read(ref count) >= 2);

        Volatile.Read(ref count).ShouldBeGreaterThanOrEqualTo(2);
    }

    private static void SpinUntil(Func<bool> condition) =>
        SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)).ShouldBeTrue();

    [Fact]
    public async Task A_stalled_catalog_body_times_out_and_keeps_the_loaded_catalog()
    {
        using var stalled = new StalledStream();
        using var service = Create(new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stalled),
        }));
        var run = service.RefreshAsync(TestContext.Current.CancellationToken);
        await stalled.Started.Task.WaitAsync(TestContext.Current.CancellationToken);
        _time.Advance(CatalogService.TransferTimeout);

        (await run).ShouldBeFalse();
        service.Current.CatalogVersion.ShouldBe("2026.10.01.1");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_signature_is_rejected_with_or_without_content_length(bool withLength)
    {
        var newer = CatalogJson("2026.10.02.1");
        var signature = Encoding.UTF8.GetBytes(Sign(newer) + new string(' ', 2048));
        var handler = new FakeHttpHandler(req =>
        {
            if (!req.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal))
            {
                return FakeHttpHandler.Ok(newer);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = withLength ? new ByteArrayContent(signature) : new StreamContent(new MemoryStream(signature)),
            };
        });
        using var service = Create(handler);

        (await service.RefreshAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        service.Current.CatalogVersion.ShouldBe("2026.10.01.1");
    }
}

public class CatalogDefaultsRegressionTests
{
    [Fact]
    public void Missing_optional_collections_deserialise_as_empty_not_null()
    {
        // STJ's source generator nulls absent init-only properties; the models use setters so defaults survive.
        var doc = JsonSerializer.Deserialize("""{"games":[{"id":"a","detection":{"exeNames":["a.exe"]}}]}""", CatalogJsonContext.Default.CatalogDocument)!;

        doc.Packs.ShouldBeEmpty();
        doc.Collections.ShouldBeEmpty();
        doc.ExcludeExeNames.ShouldBeEmpty();
        doc.Games[0].Detection.SteamAppIds.ShouldBeEmpty();
        doc.Games[0].Detection.WindowTitleContains.ShouldBeEmpty();
    }
}
