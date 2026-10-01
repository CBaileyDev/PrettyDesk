using PrettyDesk.Core.Rotation;
using Shouldly;
using Xunit;

namespace PrettyDesk.Core.Tests.Rotation;

public class ShuffleBagTests
{
    private static List<string> Pool(int n) => Enumerable.Range(0, n).Select(i => $"w{i}").ToList();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(10)]
    public void No_wallpaper_repeats_until_all_have_been_shown(int size)
    {
        var state = new ShuffleBagState();
        var random = new Random(1234);
        var pool = Pool(size);

        for (var cycle = 0; cycle < 20; cycle++)
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < size; i++)
            {
                seen.Add(ShuffleBag.Draw(state, pool, random)!).ShouldBeTrue($"cycle {cycle}, draw {i} repeated");
            }

            seen.Count.ShouldBe(size);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void Last_wallpaper_never_repeats_immediately_across_refills(int size)
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var state = new ShuffleBagState();
            var random = new Random(seed);
            var pool = Pool(size);
            string? previous = null;
            for (var i = 0; i < size * 6; i++)
            {
                var drawn = ShuffleBag.Draw(state, pool, random);
                drawn.ShouldNotBe(previous, $"seed {seed}, draw {i}");
                previous = drawn;
            }
        }
    }

    [Fact]
    public void Single_item_pool_always_returns_it()
    {
        var state = new ShuffleBagState();
        var random = new Random(1);

        for (var i = 0; i < 5; i++)
        {
            ShuffleBag.Draw(state, ["only"], random).ShouldBe("only");
        }
    }

    [Fact]
    public void Empty_pool_returns_null_and_clears_state()
    {
        var state = new ShuffleBagState { Remaining = ["a"], Shown = ["b"], Last = "b" };

        ShuffleBag.Draw(state, [], new Random(1)).ShouldBeNull();
        state.Remaining.ShouldBeEmpty();
        state.Shown.ShouldBeEmpty();
    }

    [Fact]
    public void Removed_wallpapers_leave_the_bag()
    {
        var state = new ShuffleBagState();
        var random = new Random(7);
        ShuffleBag.Draw(state, Pool(4), random);

        var smaller = Pool(4).Take(2).ToList();
        for (var i = 0; i < 6; i++)
        {
            smaller.ShouldContain(ShuffleBag.Draw(state, smaller, random)!);
        }
    }

    [Fact]
    public void Added_wallpapers_join_the_current_cycle()
    {
        var state = new ShuffleBagState();
        var random = new Random(3);
        var pool = Pool(3).ToList();
        foreach (var _ in pool)
        {
            ShuffleBag.Draw(state, pool, random);
        }

        pool.Add("new");

        ShuffleBag.Draw(state, pool, random).ShouldBe("new");
    }

    [Fact]
    public void Avoid_set_is_preferred_when_alternatives_exist()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var state = new ShuffleBagState();
            ShuffleBag.Draw(state, Pool(4), new Random(seed), new HashSet<string> { "w0", "w1", "w2" }).ShouldBe("w3");
        }
    }

    [Fact]
    public void Avoid_set_is_ignored_when_it_would_leave_nothing()
    {
        var state = new ShuffleBagState();

        ShuffleBag.Draw(state, ["a"], new Random(1), new HashSet<string> { "a" }).ShouldBe("a");
    }

    [Fact]
    public void Distribution_is_not_degenerate()
    {
        var firstDraws = new Dictionary<string, int>();
        for (var seed = 0; seed < 400; seed++)
        {
            var id = ShuffleBag.Draw(new ShuffleBagState(), Pool(4), new Random(seed))!;
            firstDraws[id] = firstDraws.GetValueOrDefault(id) + 1;
        }

        firstDraws.Count.ShouldBe(4);
        firstDraws.Values.ShouldAllBe(c => c > 50);
    }
}
