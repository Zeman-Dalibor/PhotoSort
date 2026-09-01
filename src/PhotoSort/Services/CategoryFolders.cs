using PhotoSort.Models;

namespace PhotoSort.Services;

/// <summary>Maps <see cref="PhotoCategory"/> to the sub-folder names used on disk.</summary>
public static class CategoryFolders
{
    public const string Edit = "edit";
    public const string Archive = "archive";
    public const string Delete = "delete";

    public static readonly IReadOnlyList<PhotoCategory> Filterable =
    [
        PhotoCategory.Edit, PhotoCategory.Archive, PhotoCategory.Delete
    ];

    public static string? FolderName(PhotoCategory category) => category switch
    {
        PhotoCategory.Edit => Edit,
        PhotoCategory.Archive => Archive,
        PhotoCategory.Delete => Delete,
        _ => null
    };

    public static PhotoCategory FromFolderName(string folderName)
    {
        if (string.Equals(folderName, Edit, StringComparison.OrdinalIgnoreCase)) return PhotoCategory.Edit;
        if (string.Equals(folderName, Archive, StringComparison.OrdinalIgnoreCase)) return PhotoCategory.Archive;
        if (string.Equals(folderName, Delete, StringComparison.OrdinalIgnoreCase)) return PhotoCategory.Delete;
        return PhotoCategory.None;
    }

    /// <summary>Absolute directory a photo of the given category belongs in.</summary>
    public static string ResolveDirectory(string rootPath, PhotoCategory category)
    {
        var folder = FolderName(category);
        return folder is null ? rootPath : Path.Combine(rootPath, folder);
    }
}
