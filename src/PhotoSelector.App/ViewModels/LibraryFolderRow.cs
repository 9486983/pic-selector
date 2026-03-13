namespace PhotoSelector.App.ViewModels;

public sealed class LibraryFolderRow
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string FolderPath { get; init; }
    public int PhotoCount { get; init; }
    public string CoverPath { get; init; } = string.Empty;
}
