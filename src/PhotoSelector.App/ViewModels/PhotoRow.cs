using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using PhotoSelector.Domain.Models;

namespace PhotoSelector.App.ViewModels;

public sealed class PhotoRow(PhotoItem photo) : INotifyPropertyChanged
{
    private BitmapImage? _thumbnail;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PhotoItem Photo { get; } = photo;

    public string FileName => Photo.FileName;
    public string Path => Photo.Path;
    public float OverallScore => Photo.Analysis.OverallScore;
    public float Sharpness => Photo.Analysis.SharpnessScore;
    public bool EyesClosed => Photo.Analysis.EyesClosed;
    public bool Duplicate => Photo.Analysis.IsDuplicate;
    public bool IsAnalyzed => Photo.Analysis.IsAnalyzed;
    public int FaceCount => Photo.Analysis.FaceCount;
    public string PersonLabel => Photo.Analysis.PersonLabel;
    public string StyleLabel => Photo.Analysis.StyleLabel;
    public string ColorLabel => Photo.Analysis.ColorLabel;
    public string AutoClass => Photo.Analysis.AutoClass;
    public bool IsWaste => Photo.Analysis.IsWaste;
    public string WasteReason => Photo.Analysis.WasteReason;
    public int Rating => Photo.Analysis.Rating;
    public BitmapImage? Thumbnail => _thumbnail ??= LoadBitmap(240);

    public string ScoreBadge => IsAnalyzed ? $"{OverallScore:F2}" : "--";
    public string MetaBadge => IsAnalyzed ? $"Face:{FaceCount} | {PersonLabel} | {StyleLabel} | {ColorLabel} | {AutoClass}" : "未分析";
    public string WasteBadge => IsWaste ? $"废片: {WasteReason}" : string.Empty;

    public void Refresh()
    {
        _thumbnail = null;
        OnPropertyChanged(nameof(OverallScore));
        OnPropertyChanged(nameof(Sharpness));
        OnPropertyChanged(nameof(EyesClosed));
        OnPropertyChanged(nameof(Duplicate));
        OnPropertyChanged(nameof(IsAnalyzed));
        OnPropertyChanged(nameof(FaceCount));
        OnPropertyChanged(nameof(PersonLabel));
        OnPropertyChanged(nameof(StyleLabel));
        OnPropertyChanged(nameof(ColorLabel));
        OnPropertyChanged(nameof(AutoClass));
        OnPropertyChanged(nameof(IsWaste));
        OnPropertyChanged(nameof(WasteReason));
        OnPropertyChanged(nameof(Rating));
        OnPropertyChanged(nameof(ScoreBadge));
        OnPropertyChanged(nameof(MetaBadge));
        OnPropertyChanged(nameof(WasteBadge));
    }

    private BitmapImage? LoadBitmap(int decodePixelWidth)
    {
        try
        {
            var sourcePath = string.IsNullOrWhiteSpace(Photo.ThumbnailPath) ? Path : Photo.ThumbnailPath;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.UriSource = new Uri(sourcePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
