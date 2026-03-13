using PhotoSelector.Application.Interfaces;
using PhotoSelector.Application.Services;
using PhotoSelector.Domain.Models;
using PhotoSelector.Infrastructure.Persistence;
using PhotoSelector.Infrastructure.Services;

var imageDir = args.Length > 0
    ? args[0]
    : @"D:\Cache\CodeX\test\PhotoSelectorV3\RESULT-F60";
var takeCount = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 24;

if (!Directory.Exists(imageDir))
{
    Console.WriteLine($"Image folder not found: {imageDir}");
    return 1;
}

using var client = new HttpClient
{
    BaseAddress = new Uri("http://127.0.0.1:8000")
};
IAiServiceClient ai = new HttpAiServiceClient(client);
var importer = new PhotoImportService(new ExifMetadataReader());
var coordinator = new AnalysisCoordinator(ai);
var dbPath = Path.Combine(AppContext.BaseDirectory, "smoke", "photos_smoke.db");
IPhotoRepository repo = new SqlitePhotoRepository(dbPath);

var photos = importer.ImportFromDirectory(imageDir, recursive: false).Take(takeCount).ToList();
Console.WriteLine($"Imported: {photos.Count}");
if (photos.Count == 0)
{
    return 0;
}
var exifCount = photos.Count(p => p.Metadata.Iso.HasValue || !string.IsNullOrWhiteSpace(p.Metadata.Aperture) || !string.IsNullOrWhiteSpace(p.Metadata.ShutterSpeed));
Console.WriteLine($"Imported EXIF non-empty count: {exifCount}");
var firstImported = photos.First();
Console.WriteLine($"Imported sample EXIF ISO={firstImported.Metadata.Iso}, Aperture={firstImported.Metadata.Aperture}, Shutter={firstImported.Metadata.ShutterSpeed}, Focal={firstImported.Metadata.FocalLength}, Camera={firstImported.Metadata.CameraModel}");

var batchSize = 8;
for (var i = 0; i < photos.Count; i += batchSize)
{
    var batch = photos.Skip(i).Take(batchSize).ToList();
    await coordinator.AnalyzeBatchAsync(batch);
    Console.WriteLine($"Analyzed: {Math.Min(i + batch.Count, photos.Count)}/{photos.Count}");
}

await repo.SaveAsync(photos);
var loaded = await repo.LoadAsync();
Console.WriteLine($"Loaded from SQLite: {loaded.Count}");

var sample = loaded.First();
Console.WriteLine($"Sample: {sample.FileName}");
Console.WriteLine($"EXIF ISO={sample.Metadata.Iso}, Aperture={sample.Metadata.Aperture}, Shutter={sample.Metadata.ShutterSpeed}, Focal={sample.Metadata.FocalLength}");
Console.WriteLine($"Score overall={sample.Analysis.OverallScore:F3}, sharpness={sample.Analysis.SharpnessScore:F3}");

return 0;
