using System.Text;
using Microsoft.Extensions.Options;
using Nook.Api.Ingest;
using Nook.Api.Storage;

namespace Nook.Api.Tests.Unit;

public sealed class WorkRefilingTests : IDisposable
{
    private const string OldFolder = "Books/Old Author/Old Title";
    private const string NewFolder = "Books/New Author/New Title";

    private readonly string dataPath = Path.Combine(Path.GetTempPath(), $"nook-refiling-{Guid.NewGuid():N}");
    private readonly NookStorage storage;

    public WorkRefilingTests()
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

    private void Write(string relative, string content)
    {
        var path = storage.LibraryFile(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, Encoding.UTF8);
    }

    private bool Exists(string relative) => File.Exists(storage.LibraryFile(relative));

    [Fact]
    public void Plan_puts_the_book_and_cover_in_the_folder_named_for_the_author_and_title()
    {
        var moves = WorkRefiling.Plan(
            NewFolder, "New Title", [$"{OldFolder}/Old Title.epub"], $"{OldFolder}/cover.jpg", [], storage);

        Assert.Equal(
            [
                new WorkRefiling.FileMove($"{OldFolder}/Old Title.epub", $"{NewFolder}/New Title.epub"),
                new WorkRefiling.FileMove($"{OldFolder}/cover.jpg", $"{NewFolder}/cover.jpg"),
            ],
            moves);
    }

    [Fact]
    public void Plan_takes_a_numbered_folder_when_another_works_file_is_in_the_first()
    {
        var moves = WorkRefiling.Plan(
            NewFolder,
            "New Title",
            [$"{OldFolder}/Old Title.epub"],
            null,
            [$"{NewFolder}/New Title.epub", $"{NewFolder} (2)/cover.jpg"],
            storage);

        Assert.Equal([new WorkRefiling.FileMove($"{OldFolder}/Old Title.epub", $"{NewFolder} (3)/New Title.epub")], moves);
    }

    [Fact]
    public void Plan_numbers_a_second_file_of_the_same_kind()
    {
        var moves = WorkRefiling.Plan(
            NewFolder,
            "New Title",
            [$"{OldFolder}/a.epub", $"{OldFolder}/b.pdf", $"{OldFolder}/c.epub"],
            null,
            [],
            storage);

        Assert.Equal(
            [$"{NewFolder}/New Title.epub", $"{NewFolder}/New Title.pdf", $"{NewFolder}/New Title (2).epub"],
            moves.Select(move => move.ToRelative));
    }

    [Fact]
    public void Plan_passes_over_a_name_another_file_of_the_work_is_at()
    {
        const string folder = "Books/An Author/Dune (2)";

        var moves = WorkRefiling.Plan(
            folder, "Dune (2)", [$"{folder}/Dune.epub", $"{folder}/Dune (2).epub"], null, [], storage);

        Assert.Equal([new WorkRefiling.FileMove($"{folder}/Dune.epub", $"{folder}/Dune (2) (2).epub")], moves);
    }

    [Fact]
    public void Apply_of_a_plan_inside_the_works_own_folder_keeps_every_files_bytes()
    {
        const string folder = "Books/An Author/Dune (2)";
        Write($"{folder}/Dune.epub", "first");
        Write($"{folder}/Dune (2).epub", "second");

        var moves = WorkRefiling.Plan(
            folder, "Dune (2)", [$"{folder}/Dune.epub", $"{folder}/Dune (2).epub"], null, [], storage);
        WorkRefiling.Apply(moves, storage);

        Assert.Equal("second", File.ReadAllText(storage.LibraryFile($"{folder}/Dune (2).epub")));
        Assert.Equal("first", File.ReadAllText(storage.LibraryFile($"{folder}/Dune (2) (2).epub")));
    }

    [Fact]
    public void NotClaimed_leaves_out_a_move_whose_destination_a_part_points_at()
    {
        var kept = new WorkRefiling.FileMove($"{OldFolder}/cover.jpg", $"{NewFolder}/cover.jpg");
        var moves = new[] { new WorkRefiling.FileMove($"{OldFolder}/Old Title.epub", $"{NewFolder}/New Title.epub"), kept };

        var back = WorkRefiling.NotClaimed(moves, [$"{NewFolder}/new title.epub"]);

        Assert.Equal([kept], back);
    }

    [Fact]
    public void Apply_moves_every_file_and_leaves_none_behind()
    {
        Write($"{OldFolder}/Old Title.epub", "book");
        Write($"{OldFolder}/cover.jpg", "cover");
        var moves = WorkRefiling.Plan(
            NewFolder, "New Title", [$"{OldFolder}/Old Title.epub"], $"{OldFolder}/cover.jpg", [], storage);

        WorkRefiling.Apply(moves, storage);

        Assert.Equal("book", File.ReadAllText(storage.LibraryFile($"{NewFolder}/New Title.epub")));
        Assert.Equal("cover", File.ReadAllText(storage.LibraryFile($"{NewFolder}/cover.jpg")));
        Assert.False(Exists($"{OldFolder}/Old Title.epub"));
        Assert.False(Exists($"{OldFolder}/cover.jpg"));
    }

    [Fact]
    public void Apply_puts_back_the_files_it_moved_when_one_cannot_be_moved()
    {
        Write($"{OldFolder}/Old Title.epub", "book");
        Write($"{OldFolder}/cover.jpg", "cover");
        Directory.CreateDirectory(storage.LibraryFile($"{NewFolder}/cover.jpg"));
        var moves = WorkRefiling.Plan(
            NewFolder, "New Title", [$"{OldFolder}/Old Title.epub"], $"{OldFolder}/cover.jpg", [], storage);

        Assert.ThrowsAny<Exception>(() => WorkRefiling.Apply(moves, storage));

        Assert.Equal("book", File.ReadAllText(storage.LibraryFile($"{OldFolder}/Old Title.epub")));
        Assert.Equal("cover", File.ReadAllText(storage.LibraryFile($"{OldFolder}/cover.jpg")));
        Assert.False(Exists($"{NewFolder}/New Title.epub"));
    }

    [Fact]
    public void RemoveEmptied_removes_the_work_and_author_folders_it_emptied_and_leaves_one_that_holds_a_file()
    {
        Write($"{OldFolder}/Old Title.epub", "book");
        Write("Books/Other Author/Other Title/Other.epub", "other");
        Write("Books/Other Author/Kept/Kept.epub", "kept");
        Write("Books/Other Author/Other Title/stray.txt", "stray");
        Write("Books/Half Author/Half Title/Half.epub", "half");
        Write("Books/Half Author/Sibling/Sibling.epub", "sibling");
        var moves = new List<WorkRefiling.FileMove>
        {
            new($"{OldFolder}/Old Title.epub", $"{NewFolder}/New Title.epub"),
            new("Books/Other Author/Other Title/Other.epub", $"{NewFolder}/Other.epub"),
            new("Books/Half Author/Half Title/Half.epub", $"{NewFolder}/Half.epub"),
        };
        foreach (var move in moves)
        {
            File.Delete(storage.LibraryFile(move.FromRelative));
        }

        WorkRefiling.RemoveEmptied(moves, storage);

        Assert.False(Directory.Exists(storage.LibraryFile(OldFolder)));
        Assert.False(Directory.Exists(storage.LibraryFile("Books/Old Author")));
        Assert.True(Directory.Exists(storage.LibraryFile("Books/Other Author/Other Title")));
        Assert.True(Directory.Exists(storage.LibraryFile("Books/Other Author")));
        Assert.False(Directory.Exists(storage.LibraryFile("Books/Half Author/Half Title")));
        Assert.True(Directory.Exists(storage.LibraryFile("Books/Half Author")));
        Assert.True(Directory.Exists(storage.LibraryFile("Books")));
    }
}
