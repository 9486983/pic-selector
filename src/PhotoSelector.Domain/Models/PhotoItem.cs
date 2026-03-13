namespace PhotoSelector.Domain.Models;

public sealed class PhotoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LibraryFolder { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.Now;
    public PhotoMetadata Metadata { get; set; } = new();
    public AnalysisAggregate Analysis { get; set; } = new();
}
