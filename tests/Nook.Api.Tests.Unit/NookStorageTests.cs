using Microsoft.Extensions.Options;
using Nook.Api.Storage;

namespace Nook.Api.Tests.Unit;

public sealed class NookStorageTests
{
    private static readonly string DataPath = Path.Combine(Path.GetTempPath(), "nook-storage-tests", "data");
    private static readonly string Elsewhere = Path.Combine(Path.GetTempPath(), "nook-storage-tests", "elsewhere");

    private static NookStorage Storage(string? libraryPath = null, string? cachePath = null) =>
        new(Options.Create(new StorageOptions { DataPath = DataPath, LibraryPath = libraryPath, CachePath = cachePath }));

    [Fact]
    public void Folders_without_overrides_sit_under_the_data_root()
    {
        var storage = Storage();

        Assert.Equal(Path.Combine(DataPath, "library"), storage.LibraryFolder);
        Assert.Equal(Path.Combine(DataPath, "cache"), storage.CacheFolder);
        Assert.Equal(Path.Combine(DataPath, "cache", "covers"), storage.CoversCacheFolder);
        Assert.Equal(Path.Combine(DataPath, "staging"), storage.StagingFolder);
        Assert.Equal(Path.Combine(DataPath, "keys"), storage.KeysFolder);
        Assert.Empty(storage.FindOverlappingFolders());
    }

    [Fact]
    public void Library_path_moves_only_the_library()
    {
        var library = Path.Combine(Elsewhere, "books");

        var storage = Storage(libraryPath: library);

        Assert.Equal(library, storage.LibraryFolder);
        Assert.Equal(Path.Combine(DataPath, "cache", "covers"), storage.CoversCacheFolder);
        Assert.Equal(Path.Combine(DataPath, "staging"), storage.StagingFolder);
        Assert.Equal(Path.Combine(DataPath, "keys"), storage.KeysFolder);
        Assert.Equal(
            Path.Combine(library, "Books", "Title.epub"),
            storage.LibraryFile("Books/Title.epub"));
    }

    [Fact]
    public void Library_path_that_ends_in_a_separator_still_resolves_its_files()
    {
        var library = Path.Combine(Elsewhere, "books");

        var storage = Storage(libraryPath: library + Path.DirectorySeparatorChar);

        Assert.Equal(library, storage.LibraryFolder);
        Assert.Equal(Path.Combine(library, "Books", "Title.epub"), storage.LibraryFile("Books/Title.epub"));
        Assert.Throws<InvalidOperationException>(() => storage.LibraryFile("../outside.epub"));
    }

    [Fact]
    public void Cache_path_moves_only_the_cache_with_covers_inside_it()
    {
        var cache = Path.Combine(Elsewhere, "cache");

        var storage = Storage(cachePath: cache);

        Assert.Equal(cache, storage.CacheFolder);
        Assert.Equal(Path.Combine(cache, "covers"), storage.CoversCacheFolder);
        Assert.Equal(Path.Combine(DataPath, "library"), storage.LibraryFolder);
        Assert.Equal(Path.Combine(DataPath, "staging"), storage.StagingFolder);
        Assert.Equal(Path.Combine(DataPath, "keys"), storage.KeysFolder);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_overrides_fall_back_to_the_folders_under_the_data_root(string blank)
    {
        var storage = Storage(libraryPath: blank, cachePath: blank);

        Assert.Equal(Path.Combine(DataPath, "library"), storage.LibraryFolder);
        Assert.Equal(Path.Combine(DataPath, "cache", "covers"), storage.CoversCacheFolder);
        Assert.All(storage.Folders, folder => Assert.Equal("Nook:DataPath", folder.Setting));
    }

    [Fact]
    public void Folders_name_the_setting_that_places_them()
    {
        var storage = Storage(libraryPath: Path.Combine(Elsewhere, "books"), cachePath: Path.Combine(Elsewhere, "cache"));

        Assert.Equal(
            [("library", "Nook:LibraryPath"), ("cache", "Nook:CachePath"), ("staging", "Nook:DataPath"), ("keys", "Nook:DataPath")],
            storage.Folders.Select(folder => (folder.Name, folder.Setting)));
    }

    [Fact]
    public void Cache_inside_the_library_is_an_overlap_naming_both_settings()
    {
        var library = Path.Combine(Elsewhere, "books");

        var problems = Storage(libraryPath: library, cachePath: Path.Combine(library, "cache")).FindOverlappingFolders();

        var problem = Assert.Single(problems);
        Assert.Contains("Nook:CachePath", problem);
        Assert.Contains("Nook:LibraryPath", problem);
        Assert.Contains($"'{library}'", problem);
    }

    [Fact]
    public void Library_at_the_data_root_overlaps_the_cache_staging_and_keys()
    {
        var problems = Storage(libraryPath: DataPath).FindOverlappingFolders();

        Assert.Equal(3, problems.Count);
        Assert.All(problems, problem => Assert.Contains("Nook:LibraryPath", problem));
        Assert.All(problems, problem => Assert.Contains("Nook:DataPath", problem));
    }

    [Fact]
    public void Library_and_cache_at_the_same_folder_overlap()
    {
        var shared = Path.Combine(Elsewhere, "shared") + Path.DirectorySeparatorChar;

        var problem = Assert.Single(Storage(libraryPath: shared, cachePath: Path.Combine(Elsewhere, "shared")).FindOverlappingFolders());

        Assert.Contains("same folder", problem);
    }

    [Fact]
    public void Folders_whose_names_share_a_start_do_not_overlap()
    {
        var storage = Storage(libraryPath: Path.Combine(Elsewhere, "lib"), cachePath: Path.Combine(Elsewhere, "library"));

        Assert.Empty(storage.FindOverlappingFolders());
    }

    [Fact]
    public void Validator_refuses_overlapping_folders_and_leaves_a_missing_data_root_to_its_own_check()
    {
        var validator = new StorageOptionsValidator();

        var overlapping = validator.Validate(null, new StorageOptions { DataPath = DataPath, CachePath = Path.Combine(DataPath, "library", "cache") });
        var missing = validator.Validate(null, new StorageOptions { DataPath = "" });
        var apart = validator.Validate(null, new StorageOptions { DataPath = DataPath });

        Assert.True(overlapping.Failed);
        Assert.True(missing.Skipped);
        Assert.True(apart.Succeeded);
    }
}
