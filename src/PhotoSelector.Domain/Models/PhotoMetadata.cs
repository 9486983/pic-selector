namespace PhotoSelector.Domain.Models;

public sealed class PhotoMetadata
{
    public int? Iso { get; set; }
    public string? Aperture { get; set; }
    public string? ShutterSpeed { get; set; }
    public string? FocalLength { get; set; }
    public string? WhiteBalance { get; set; }
    public DateTimeOffset? CapturedAt { get; set; }
    public string? CameraModel { get; set; }
    public string? LensModel { get; set; }
}
