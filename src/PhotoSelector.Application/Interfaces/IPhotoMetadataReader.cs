using PhotoSelector.Domain.Models;

namespace PhotoSelector.Application.Interfaces;

public interface IPhotoMetadataReader
{
    PhotoMetadata Read(string path);
}
