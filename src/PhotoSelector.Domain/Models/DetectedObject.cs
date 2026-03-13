namespace PhotoSelector.Domain.Models;

public sealed class DetectedObject
{
    public string Label { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
}
