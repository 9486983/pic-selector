using PhotoSelector.Domain.Models;

namespace PhotoSelector.Application.Interfaces;

public interface IPhotoRepository
{
    Task SaveAsync(IReadOnlyCollection<PhotoItem> photos, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PhotoItem>> LoadAsync(CancellationToken cancellationToken = default);
}
