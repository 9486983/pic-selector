using PhotoSelector.Application.Interfaces;
using PhotoSelector.Domain.Models;

namespace PhotoSelector.Application.Services;

public sealed class PhotoImportService(IPhotoMetadataReader metadataReader)
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff",
        ".cr2", ".cr3", ".nef", ".arw", ".dng", ".rwa", ".rw2", ".orf", ".raf", ".sr2", ".srw", ".pef", ".3fr"
    };

    public IReadOnlyCollection<PhotoItem> ImportFromDirectory(string directory, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Array.Empty<PhotoItem>();
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directory, "*.*", option)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new List<PhotoItem>(files.Count);
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            var metadata = metadataReader.Read(info.FullName);
            metadata.CapturedAt ??= info.CreationTimeUtc;
            result.Add(new PhotoItem
            {
                LibraryFolder = Path.GetFullPath(directory),
                Path = info.FullName,
                FileName = info.Name,
                Metadata = metadata
            });
        }

        return result;
    }
}
