using PhotoSort.Models;
using PhotoSort.Services;

namespace PhotoSort.Tests;

public sealed class PhotoLibraryTests
{
    private readonly PhotoScanner _scanner = new();

    [Fact]
    public void StopsAtBothEndsInsteadOfWrappingAround()
    {
        using var folder = new TempFolder();
        var library = Load(folder, "a.jpg", "b.jpg");

        Assert.False(library.MovePrevious());
        Assert.True(library.MoveNext());
        Assert.False(library.MoveNext());
        Assert.Equal(1, library.CurrentIndex);
    }

    [Fact]
    public void ExposesTheNeighboursOfTheCurrentPhoto()
    {
        using var folder = new TempFolder();
        var library = Load(folder, "a.jpg", "b.jpg", "c.jpg");
        library.MoveNext();

        Assert.Equal("a", library.Previous?.DisplayName);
        Assert.Equal("b", library.Current?.DisplayName);
        Assert.Equal("c", library.Next?.DisplayName);
    }

    [Fact]
    public void CategorisingReportsEveryPathChange()
    {
        using var folder = new TempFolder();
        folder.CreateFile("a.JPG");
        folder.CreateFile("a.CR2");
        var library = new PhotoLibrary(new PhotoFileMover());
        library.Load(folder.Path, _scanner.Scan(folder.Path, includeFilterFolders: false));

        var relocations = new List<(string Old, string New)>();
        library.PhotoRelocated += (oldPath, newPath) => relocations.Add((oldPath, newPath));

        library.Categorise(PhotoCategory.Edit);

        Assert.Equal(2, relocations.Count);
        Assert.All(relocations, r => Assert.Contains($"{Path.DirectorySeparatorChar}edit{Path.DirectorySeparatorChar}", r.New));
    }

    [Fact]
    public void UndoRestoresTheCategoryAndSelectsThePhotoAgain()
    {
        using var folder = new TempFolder();
        var library = Load(folder, "a.jpg", "b.jpg");
        var first = library.Current!;

        library.Categorise(PhotoCategory.Delete);
        library.MoveNext();
        Assert.True(library.CanUndo);

        library.Undo();

        Assert.Equal(PhotoCategory.None, first.Category);
        Assert.Same(first, library.Current);
        Assert.False(library.CanUndo);
    }

    [Fact]
    public void CategorisingIntoTheCurrentCategoryIsANoOp()
    {
        using var folder = new TempFolder();
        var library = Load(folder, "a.jpg");

        Assert.Null(library.Categorise(PhotoCategory.None));
        Assert.False(library.CanUndo);
    }

    private PhotoLibrary Load(TempFolder folder, params string[] files)
    {
        foreach (var file in files)
        {
            folder.CreateFile(file);
        }

        var library = new PhotoLibrary(new PhotoFileMover());
        library.Load(folder.Path, _scanner.Scan(folder.Path, includeFilterFolders: false));
        return library;
    }
}
