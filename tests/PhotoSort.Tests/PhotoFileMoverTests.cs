using PhotoSort.Models;
using PhotoSort.Services;

namespace PhotoSort.Tests;

public sealed class PhotoFileMoverTests
{
    private readonly PhotoFileMover _mover = new();
    private readonly PhotoScanner _scanner = new();

    [Fact]
    public void MovesEveryVariantOfAGroupTogether()
    {
        using var folder = new TempFolder();
        folder.CreateFile("IMG_0042.JPG");
        folder.CreateFile("IMG_0042.CR2");
        var item = Assert.Single(_scanner.Scan(folder.Path, includeFilterFolders: false));

        _mover.Move(item, PhotoCategory.Edit, folder.Path);

        Assert.True(File.Exists(Path.Combine(folder.Path, "edit", "IMG_0042.JPG")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "edit", "IMG_0042.CR2")));
        Assert.False(File.Exists(Path.Combine(folder.Path, "IMG_0042.JPG")));
        Assert.Equal(PhotoCategory.Edit, item.Category);
    }

    [Fact]
    public void CreatesTheTargetFolderOnDemand()
    {
        using var folder = new TempFolder();
        folder.CreateFile("a.jpg");
        var item = Assert.Single(_scanner.Scan(folder.Path, includeFilterFolders: false));

        _mover.Move(item, PhotoCategory.Delete, folder.Path);

        Assert.True(Directory.Exists(Path.Combine(folder.Path, "delete")));
    }

    [Fact]
    public void ReturnsNullWhenTheItemIsAlreadyInTheTargetCategory()
    {
        using var folder = new TempFolder();
        folder.CreateFile("archive/a.jpg");
        var item = Assert.Single(_scanner.Scan(folder.Path, includeFilterFolders: true));

        Assert.Null(_mover.Move(item, PhotoCategory.Archive, folder.Path));
    }

    [Fact]
    public void KeepsVariantsPairedWhenResolvingANameCollision()
    {
        using var folder = new TempFolder();
        folder.CreateFile("edit/IMG_1.JPG");
        folder.CreateFile("IMG_1.JPG");
        folder.CreateFile("IMG_1.CR2");

        var item = _scanner
            .Scan(folder.Path, includeFilterFolders: false)
            .Single(i => i.Variants.Count == 2);

        _mover.Move(item, PhotoCategory.Edit, folder.Path);

        Assert.True(File.Exists(Path.Combine(folder.Path, "edit", "IMG_1 (1).JPG")));
        Assert.True(File.Exists(Path.Combine(folder.Path, "edit", "IMG_1 (1).CR2")));
    }

    [Fact]
    public void UndoRestoresTheOriginalPaths()
    {
        using var folder = new TempFolder();
        var originalJpg = folder.CreateFile("IMG_7.JPG");
        var originalRaw = folder.CreateFile("IMG_7.CR2");
        var item = Assert.Single(_scanner.Scan(folder.Path, includeFilterFolders: false));

        var record = _mover.Move(item, PhotoCategory.Delete, folder.Path);
        _mover.Undo(record!);

        Assert.True(File.Exists(originalJpg));
        Assert.True(File.Exists(originalRaw));
        Assert.Equal(PhotoCategory.None, item.Category);
    }

    [Fact]
    public void MovesBackToTheRootFolder()
    {
        using var folder = new TempFolder();
        folder.CreateFile("delete/a.jpg");
        var item = Assert.Single(_scanner.Scan(folder.Path, includeFilterFolders: true));

        _mover.Move(item, PhotoCategory.None, folder.Path);

        Assert.True(File.Exists(Path.Combine(folder.Path, "a.jpg")));
        Assert.Equal(PhotoCategory.None, item.Category);
    }
}
