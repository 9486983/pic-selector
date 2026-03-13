using System.Text.Json;
using PhotoSelector.Application.Interfaces;
using PhotoSelector.Domain.Models;

namespace PhotoSelector.Infrastructure.Persistence;

public sealed class JsonPhotoRepository(string storageFilePath) : IPhotoRepository
{
    public async Task SaveAsync(IReadOnlyCollection<PhotoItem> photos, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(storageFilePath);
        await JsonSerializer.SerializeAsync(stream, photos, cancellationToken: cancellationToken, options: new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public async Task<IReadOnlyCollection<PhotoItem>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(storageFilePath))
        {
            return Array.Empty<PhotoItem>();
        }

        await using var stream = File.OpenRead(storageFilePath);
        var photos = await JsonSerializer.DeserializeAsync<List<PhotoItem>>(stream, cancellationToken: cancellationToken);
        return photos ?? new List<PhotoItem>();
    }
}
