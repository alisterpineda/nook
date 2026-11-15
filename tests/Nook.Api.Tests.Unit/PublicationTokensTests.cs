using Microsoft.Extensions.Options;
using Nook.Api.Features.Publications;
using Nook.Api.Storage;

namespace Nook.Api.Tests.Unit;

public sealed class PublicationTokensTests : IDisposable
{
    private const string UserId = "3f6c1d2e-8a4b-4c5d-9e7f-0a1b2c3d4e5f";

    private static readonly Guid ItemId = Guid.Parse("a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d");

    private readonly string dataPath = Path.Combine(Path.GetTempPath(), $"nook-tokens-{Guid.NewGuid():N}");
    private readonly NookStorage storage;
    private readonly SetClock clock = new(new DateTimeOffset(2026, 11, 15, 9, 30, 0, TimeSpan.Zero));

    public PublicationTokensTests()
    {
        storage = new NookStorage(Options.Create(new StorageOptions { DataPath = dataPath }));
        storage.CreateFolders();
    }

    public void Dispose()
    {
        if (Directory.Exists(dataPath))
        {
            Directory.Delete(dataPath, recursive: true);
        }
    }

    [Fact]
    public void Token_is_the_same_for_the_same_user_item_and_day()
    {
        var tokens = new PublicationTokens(storage, clock);

        var morning = tokens.Issue(ItemId, UserId);
        clock.Now = new DateTimeOffset(2026, 11, 15, 23, 59, 59, TimeSpan.Zero);
        var evening = tokens.Issue(ItemId, UserId);

        Assert.Equal(morning, evening);
        Assert.NotEqual(morning, tokens.Issue(ItemId, "another-user"));
        Assert.NotEqual(morning, tokens.Issue(Guid.NewGuid(), UserId));
        Assert.Matches("^[A-Za-z0-9_-]+$", morning);
    }

    [Fact]
    public void Token_is_read_back_on_its_day_and_the_next()
    {
        var tokens = new PublicationTokens(storage, clock);
        var token = tokens.Issue(ItemId, UserId);

        Assert.True(tokens.TryRead(token, ItemId, out var sameDayUser));
        Assert.Equal(UserId, sameDayUser);

        clock.Now = new DateTimeOffset(2026, 11, 16, 23, 59, 59, TimeSpan.Zero);
        Assert.True(tokens.TryRead(token, ItemId, out var nextDayUser));
        Assert.Equal(UserId, nextDayUser);
    }

    [Fact]
    public void Token_is_refused_two_days_later()
    {
        var tokens = new PublicationTokens(storage, clock);
        var token = tokens.Issue(ItemId, UserId);

        clock.Now = new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);

        Assert.False(tokens.TryRead(token, ItemId, out _));
    }

    [Fact]
    public void Token_for_another_item_is_refused()
    {
        var tokens = new PublicationTokens(storage, clock);
        var token = tokens.Issue(ItemId, UserId);

        Assert.False(tokens.TryRead(token, Guid.NewGuid(), out _));
    }

    [Fact]
    public void Changed_token_is_refused()
    {
        var tokens = new PublicationTokens(storage, clock);
        var token = tokens.Issue(ItemId, UserId);

        // Every single-character change, plus a truncated token, a lengthened one and things that are not tokens.
        var changed = Enumerable.Range(0, token.Length)
            .Select(i => token[..i] + (token[i] == 'A' ? 'B' : 'A') + token[(i + 1)..])
            .Concat([token[..^1], token + "A", "", "not a token", "%2F", "../manifest.json"]);

        Assert.All(changed, candidate => Assert.False(tokens.TryRead(candidate, ItemId, out _), candidate));
    }

    [Fact]
    public void Secret_survives_a_new_instance_on_the_same_folder()
    {
        var token = new PublicationTokens(storage, clock).Issue(ItemId, UserId);

        var again = new PublicationTokens(storage, clock);

        Assert.Equal(token, again.Issue(ItemId, UserId));
        Assert.True(again.TryRead(token, ItemId, out _));

        var elsewhere = new NookStorage(Options.Create(new StorageOptions
        {
            DataPath = Path.Combine(dataPath, "elsewhere"),
        }));
        Assert.NotEqual(token, new PublicationTokens(elsewhere, clock).Issue(ItemId, UserId));
    }

    [Fact]
    public void Secret_file_left_unfinished_is_replaced()
    {
        var path = Path.Combine(storage.KeysFolder, "publication-tokens.key");
        File.WriteAllBytes(path, []);

        var token = new PublicationTokens(storage, clock).Issue(ItemId, UserId);

        Assert.Equal(32, File.ReadAllBytes(path).Length);
        Assert.Equal(token, new PublicationTokens(storage, clock).Issue(ItemId, UserId));
    }

    [Fact]
    public void Concurrent_first_use_settles_on_one_secret()
    {
        var instances = Enumerable.Range(0, 16).Select(_ => new PublicationTokens(storage, clock)).ToArray();

        var issued = instances.AsParallel().Select(tokens => tokens.Issue(ItemId, UserId)).ToArray();

        Assert.Single(issued.Distinct());
    }

    /// <summary>A clock that reads the time the test sets.</summary>
    private sealed class SetClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
