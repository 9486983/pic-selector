namespace PhotoSelector.Domain.Models;

public sealed class PluginResult
{
    public string PluginName { get; set; } = string.Empty;
    public float Score { get; set; }
    public Dictionary<string, object> Features { get; set; } = new();
    public List<DetectedObject> Objects { get; set; } = new();
}
