using System.Text;
using Microsoft.Extensions.Options;
using Nook.Api.Data;
using Nook.Api.Ingest;
using Nook.Api.Storage;

namespace Nook.Api.Tests.Unit;

public sealed class FilingTests : IDisposable
{
    private const string Folder = "Books/Ursula K. Le Guin/The Dispossessed";

    private readonly string dataPath = Path.Combine(Path.GetTempPath(), $"nook-filing-{Guid.NewGuid():N}");
    private readonly Library library = new() { Name = "Books", FolderName = "books" };
    private readonly NookStorage storage;

    public FilingTests()
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

    [Theory]
    [InlineData('<')]
    [InlineData('>')]
    [InlineData(':')]
    [InlineData('"')]
    [InlineData('/')]
    [InlineData('\\')]
    [InlineData('|')]
    [InlineData('?')]
    [InlineData('*')]
    [InlineData('\0')]
    [InlineData('\t')]
    [InlineData('\u001f')]
    [InlineData('\u007f')]
    public void Illegal_characters_are_replaced(char illegal)
    {
        Assert.Equal("Before_After", Filing.Segment($"Before{illegal}After"));
    }

    [Theory]
    [InlineData("Title.", "Title")]
    [InlineData("Title...", "Title")]
    [InlineData("Title . . ", "Title")]
    [InlineData("  Title  ", "Title")]
    [InlineData("What? A \"Test\": Part 1/2...", "What_ A _Test__ Part 1_2")]
    [InlineData("J. R. R.", "J. R. R")]
    public void Trailing_dots_and_spaces_are_trimmed(string name, string expected)
    {
        Assert.Equal(expected, Filing.Segment(name));
    }

    [Fact]
    public void Long_name_is_cut_to_about_100_characters()
    {
        var segment = Filing.Segment(new string('a', 150));

        Assert.Equal(new string('a', 100), segment);
    }

    [Fact]
    public void Long_name_of_multibyte_characters_stays_within_200_bytes_and_ends_on_a_whole_character()
    {
        var name = string.Concat(Enumerable.Repeat("€", 150));

        var segment = Filing.Segment(name);

        Assert.InRange(Encoding.UTF8.GetByteCount(segment), 1, 200);
        Assert.Equal(string.Concat(Enumerable.Repeat("€", segment.Length)), segment);
        Assert.Equal(66, segment.Length);
    }

    [Fact]
    public void Long_name_is_not_cut_inside_a_surrogate_pair()
    {
        var name = "a" + string.Concat(Enumerable.Repeat("\U0001F4DA", 60));

        var segment = Filing.Segment(name);

        Assert.True(segment.Length <= 100);
        Assert.True(Encoding.UTF8.GetByteCount(segment) <= 200);
        Assert.False(char.IsHighSurrogate(segment[^1]), "The segment ends with half a character.");
        Assert.Equal(segment, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(segment)));
    }

    [Fact]
    public void Name_cut_before_spaces_or_dots_is_trimmed_again()
    {
        var segment = Filing.Segment(new string('a', 98) + " ." + new string('b', 20));

        Assert.Equal(new string('a', 98), segment);
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul", "_nul")]
    [InlineData("Aux", "_Aux")]
    [InlineData("PRN", "_PRN")]
    [InlineData("COM1", "_COM1")]
    [InlineData("lpt9", "_lpt9")]
    [InlineData("CON.tar", "_CON.tar")]
    [InlineData("CON.", "_CON")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("COM10", "COM10")]
    public void Reserved_windows_name_is_changed(string name, string expected)
    {
        Assert.Equal(expected, Filing.Segment(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData(" . . ")]
    public void Empty_name_becomes_untitled(string name)
    {
        Assert.Equal("Untitled", Filing.Segment(name));
    }

    [Fact]
    public void Work_folder_makes_each_segment_safe()
    {
        Assert.Equal("Books/A_B/What_ A _Test__ Part 1_2", Filing.WorkFolder("A/B", "What? A \"Test\": Part 1/2..."));
    }

    [Fact]
    public void Work_folder_without_an_author_is_under_unknown_author()
    {
        Assert.Equal("Books/Unknown Author/mystery-book", Filing.WorkFolder(null, "mystery-book"));
    }

    [Fact]
    public void Unclaimed_file_at_the_name_is_overwritten()
    {
        var left = WriteLibraryFile($"{Folder}/The Dispossessed.epub", "partial");
        var book = Stage("uploaded");

        var filed = Filing.CopyBesideOthers(book, Folder);

        Assert.Equal($"{Folder}/The Dispossessed.epub", filed.RelativePath);
        Assert.Equal(left, filed.FullPath);
        Assert.Equal("uploaded", File.ReadAllText(filed.FullPath));
        Assert.Null(filed.CreatedFolder);
        Assert.Equal("uploaded", File.ReadAllText(book.StagedFile));
    }

    [Theory]
    [InlineData($"{Folder}/The Dispossessed.epub")]
    [InlineData("BOOKS/ursula k. le guin/THE DISPOSSESSED/the dispossessed.EPUB")]
    public void Claimed_file_at_the_name_gets_the_next_number(string claimedPath)
    {
        var taken = WriteLibraryFile($"{Folder}/The Dispossessed.epub", "already filed");
        var book = Stage("uploaded", claimedPath);

        var filed = Filing.CopyBesideOthers(book, Folder);

        Assert.Equal($"{Folder}/The Dispossessed (2).epub", filed.RelativePath);
        Assert.Equal("uploaded", File.ReadAllText(filed.FullPath));
        Assert.Equal("already filed", File.ReadAllText(taken));
        Assert.Equal("uploaded", File.ReadAllText(book.StagedFile));
    }

    [Fact]
    public void Unclaimed_folder_is_reused_and_other_files_in_it_are_left_alone()
    {
        WriteLibraryFile($"{Folder}/The Dispossessed.epub", "partial");
        var notes = WriteLibraryFile($"{Folder}/notes.txt", "kept");
        var book = Stage("uploaded", "Books/Ursula K. Le Guin/The Lathe of Heaven/The Lathe of Heaven.epub");

        var filed = Filing.CopyIntoNewFolder(book, Folder);

        Assert.Equal($"{Folder}/The Dispossessed.epub", filed.RelativePath);
        Assert.Null(filed.CreatedFolder);
        Assert.Equal("uploaded", File.ReadAllText(filed.FullPath));
        Assert.Equal("kept", File.ReadAllText(notes));
        Assert.Equal(
            [$"{Folder}/The Dispossessed.epub", $"{Folder}/notes.txt"],
            LibraryFiles().Where(path => path.StartsWith(Folder + "/", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData($"{Folder}/The Dispossessed.epub")]
    [InlineData("books/URSULA K. LE GUIN/the dispossessed/Other Edition.epub")]
    public void Claimed_folder_gets_the_next_number(string claimedPath)
    {
        var taken = WriteLibraryFile($"{Folder}/The Dispossessed.epub", "already filed");
        var book = Stage("uploaded", claimedPath);

        var filed = Filing.CopyIntoNewFolder(book, Folder);

        Assert.Equal($"{Folder} (2)/The Dispossessed.epub", filed.RelativePath);
        Assert.Equal(storage.LibraryFile(library, $"{Folder} (2)"), filed.CreatedFolder);
        Assert.Equal("uploaded", File.ReadAllText(filed.FullPath));
        Assert.Equal("already filed", File.ReadAllText(taken));
        Assert.Equal("uploaded", File.ReadAllText(book.StagedFile));
    }

    [Fact]
    public void Name_is_put_in_one_unicode_form()
    {
        Assert.Equal("Caf\u00e9", Filing.Segment("Cafe\u0301"));
    }

    [Theory]
    [InlineData(0xFFFE)]
    [InlineData(0xD800)]
    [InlineData(0xDC00)]
    public void Code_points_that_are_not_characters_are_replaced(int code)
    {
        // Half a surrogate pair cannot be written in an attribute, so the name is put together here.
        Assert.Equal("a_b", Filing.Segment($"a{(char)code}b"));
    }

    [Fact]
    public void Path_stored_with_a_code_point_that_is_not_a_character_is_still_compared()
    {
        Assert.True(Filing.IsClaimed(["Books/a\uFFFEb/Book.epub"], "books/A\uFFFEB/book.epub"));
        Assert.False(Filing.IsClaimed(["Books/a\uFFFEb/Book.epub"], "Books/a_b/Book.epub"));
    }

    [Fact]
    public void File_claimed_under_another_unicode_form_of_its_name_gets_the_next_number()
    {
        // Some file systems treat the two spellings as one name, so the claimed file must not be written over.
        const string composed = "Books/Ursula K. Le Guin/Caf\u00e9";
        const string decomposed = "Books/Ursula K. Le Guin/Cafe\u0301";
        var taken = WriteLibraryFile($"{composed}/The Dispossessed.epub", "already filed");
        var book = Stage("uploaded", $"{decomposed}/The Dispossessed.epub");

        var filed = Filing.CopyBesideOthers(book, composed);

        Assert.Equal($"{composed}/The Dispossessed (2).epub", filed.RelativePath);
        Assert.Equal("already filed", File.ReadAllText(taken));
    }

    [Fact]
    public void Folder_claimed_under_another_unicode_form_of_its_name_gets_the_next_number()
    {
        const string composed = "Books/Ursula K. Le Guin/Caf\u00e9";
        var taken = WriteLibraryFile($"{composed}/The Dispossessed.epub", "already filed");
        var book = Stage("uploaded", "Books/Ursula K. Le Guin/Cafe\u0301/Other Edition.epub");

        var filed = Filing.CopyIntoNewFolder(book, composed);

        Assert.Equal($"{composed} (2)/The Dispossessed.epub", filed.RelativePath);
        Assert.Equal("already filed", File.ReadAllText(taken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_staged_file_fails_and_files_nothing(bool intoNewFolder)
    {
        // The book's own name is taken, so a numbered name is the one a wrong retry would use.
        WriteLibraryFile($"{Folder}/The Dispossessed.epub", "already filed");
        var book = Stage("uploaded", $"{Folder}/The Dispossessed.epub");
        File.Delete(book.StagedFile);
        var before = LibraryEntries();

        Assert.Throws<FileNotFoundException>(() => intoNewFolder
            ? Filing.CopyIntoNewFolder(book, Folder)
            : Filing.CopyBesideOthers(book, Folder));

        Assert.Equal(before, LibraryEntries());
    }

    /// <summary>Stages a file to be filed as "The Dispossessed", with the library's parts at the claimed paths.</summary>
    private Filing.StagedBook Stage(string content, params string[] claimedPaths)
    {
        var stagedFile = storage.StagedFile(Guid.NewGuid().ToString());
        File.WriteAllText(stagedFile, content);
        return new Filing.StagedBook(stagedFile, "The Dispossessed", ".epub", library, storage, claimedPaths);
    }

    private string WriteLibraryFile(string relativePath, string content)
    {
        var path = storage.LibraryFile(library, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Every file under the library's folder, as a <c>/</c>-separated path relative to it, in order.</summary>
    private string[] LibraryFiles()
    {
        var folder = storage.LibraryFolder(library);
        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Every file and folder under the library's folder, relative to it, in order.</summary>
    private string[] LibraryEntries()
    {
        var folder = storage.LibraryFolder(library);
        return Directory.Exists(folder)
            ? Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                .Select(entry => Path.GetRelativePath(folder, entry))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
    }
}
