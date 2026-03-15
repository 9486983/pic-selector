using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using PhotoSelector.Application.Interfaces;
using PhotoSelector.Domain.Models;

namespace PhotoSelector.Infrastructure.Services;

public sealed class ExifMetadataReader : IPhotoMetadataReader
{
    public PhotoMetadata Read(string path)
    {
        var metadata = new PhotoMetadata();
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var exifIfd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            var exifSubIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();

            if (exifSubIfd is not null)
            {
                if (exifSubIfd.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso))
                {
                    metadata.Iso = iso;
                }

                metadata.Aperture = exifSubIfd.GetDescription(ExifDirectoryBase.TagFNumber)
                    ?? exifSubIfd.GetDescription(ExifDirectoryBase.TagAperture);
                metadata.ShutterSpeed = exifSubIfd.GetDescription(ExifDirectoryBase.TagExposureTime)
                    ?? exifSubIfd.GetDescription(ExifDirectoryBase.TagShutterSpeed);
                metadata.FocalLength = exifSubIfd.GetDescription(ExifDirectoryBase.TagFocalLength);
                metadata.WhiteBalance = exifSubIfd.GetDescription(ExifDirectoryBase.TagWhiteBalance);
                metadata.LensModel = exifSubIfd.GetDescription(ExifDirectoryBase.TagLensModel);

                if (exifSubIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                {
                    metadata.CapturedAt = new DateTimeOffset(dt);
                }
            }

            metadata.CameraMake = exifIfd0?.GetDescription(ExifDirectoryBase.TagMake);
            metadata.CameraModel = exifIfd0?.GetDescription(ExifDirectoryBase.TagModel);
            return metadata;
        }
        catch
        {
            return metadata;
        }
    }
}
