using PhotoSort.Models;
using PhotoSort.Services;

namespace PhotoSort.Tests;

public sealed class PhotoScannerTests
{
    [Fact]
    public void GroupsJpgAndCr2WithTheSameBaseNameIntoOneItem()
    {
        using var folder = new TempFolder();
        folder.CreateFile("IMG_0042.JPG");
        folder.CreateFile("IMG_0042.CR2");

        var items = new PhotoScanner().Scan(folder.Path, includeFilterFolders: false);

        var item = Assert.Single(items);
        Assert.Equal("IMG_0042", item.DisplayName);
        Assert.Equal(2, item.Variants.Count);
    }

    [Fact]
    public void PrefersTheRasterVariantForDisplay()
    {
        using var folder = new TempFolder();
        folder.CreateFile("IMG_1.CR2");
        folder.CreateFile("IMG_1.JPG");

        var item = Assert.Single(new PhotoScanner().Scan(folder.Path, includeFilterFolders: false));

        Assert.Equal(".JPG", item.SelectedVariant.Extension);
    }

    [Fact]
    public void IgnoresUnsupportedExtensions()
    {
        using var folder = new TempFolder();
        folder.CreateFile("notes.txt");
        folder.CreateFile("clip.mp4");
        folder.CreateFile("photo.jpg");

        var items = new PhotoScanner().Scan(folder.Path, includeFilterFolders: false);

        Assert.Single(items);
    }

    [Fact]
    public void SkipsFilterFoldersUnlessRequested()
    {
        using var folder = new TempFolder();
        folder.CreateFile("root.jpg");
        folder.CreateFile("edit/edited.jpg");
        folder.CreateFile("archive/kept.jpg");
        folder.CreateFile("delete/dropped.jpg");

        var withoutFiltered = new PhotoScanner().Scan(folder.Path, includeFilterFolders: false);
        var withFiltered = new PhotoScanner().Scan(folder.Path, includeFilterFolders: true);

        Assert.Single(withoutFiltered);
        Assert.Equal(4, withFiltered.Count);
    }

    [Fact]
    public void AssignsCategoryFromTheContainingFolder()
    {
        using var folder = new TempFolder();
        folder.CreateFile("archive/kept.jpg");

        var item = Assert.Single(new PhotoScanner().Scan(folder.Path, includeFilterFolders: true));

        Assert.Equal(PhotoCategory.Archive, item.Category);
    }

    [Fact]
    public void OrdersNamesNaturally()
    {
        using var folder = new TempFolder();
        folder.CreateFile("IMG_10.jpg");
        folder.CreateFile("IMG_2.jpg");
        folder.CreateFile("IMG_1.jpg");

        var names = new PhotoScanner()
            .Scan(folder.Path, includeFilterFolders: false)
            .Select(i => i.DisplayName)
            .ToArray();

        Assert.Equal(["IMG_1", "IMG_2", "IMG_10"], names);
    }

    [Fact]
    public void PutsUnsortedPhotosBeforeAlreadyCategorisedOnes()
    {
        using var folder = new TempFolder();
        folder.CreateFile("archive/aaa.jpg");
        folder.CreateFile("zzz.jpg");

        var items = new PhotoScanner().Scan(folder.Path, includeFilterFolders: true);

        Assert.Equal(PhotoCategory.None, items[0].Category);
        Assert.Equal(PhotoCategory.Archive, items[1].Category);
    }
}
