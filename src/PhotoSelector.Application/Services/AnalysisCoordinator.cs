using PhotoSelector.Application.Interfaces;
using PhotoSelector.Domain.Models;

namespace PhotoSelector.Application.Services;

public sealed class AnalysisCoordinator(IAiServiceClient aiClient)
{
    public async Task AnalyzeAsync(PhotoItem photo, CancellationToken cancellationToken = default)
    {
        var response = await aiClient.AnalyzeAsync(photo.Path, cancellationToken);
        ApplyResponse(photo, response);
    }

    public async Task AnalyzeBatchAsync(IReadOnlyCollection<PhotoItem> photos, CancellationToken cancellationToken = default)
    {
        var byPath = photos.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var responses = await aiClient.AnalyzeBatchAsync(byPath.Keys.ToList(), cancellationToken);
        foreach (var pair in responses)
        {
            if (byPath.TryGetValue(pair.ImagePath, out var photo))
            {
                ApplyResponse(photo, pair.Response);
            }
        }
    }

    public async Task AnalyzeBatchAsync(
        IReadOnlyCollection<PhotoItem> photos,
        Func<PhotoItem, string> pathResolver,
        CancellationToken cancellationToken = default)
    {
        var photoList = photos.ToList();
        var analysisPaths = photoList.Select(pathResolver).ToList();
        var responseMap = (await aiClient.AnalyzeBatchAsync(analysisPaths, cancellationToken))
            .ToDictionary(x => x.ImagePath, x => x.Response, StringComparer.OrdinalIgnoreCase);

        foreach (var photo in photoList)
        {
            var path = pathResolver(photo);
            if (responseMap.TryGetValue(path, out var response))
            {
                ApplyResponse(photo, response);
            }
        }
    }

    private static void ApplyResponse(PhotoItem photo, AnalyzeResponse response)
    {
        var existingRating = photo.Analysis.Rating;
        photo.Analysis = new AnalysisAggregate
        {
            IsAnalyzed = true,
            OverallScore = response.OverallScore,
            SharpnessScore = response.SharpnessScore,
            ExposureScore = response.ExposureScore,
            EyesClosed = response.EyesClosed,
            IsDuplicate = response.IsDuplicate,
            FaceCount = response.FaceCount,
            PersonLabel = response.PersonLabel,
            StyleLabel = response.StyleLabel,
            ColorLabel = response.ColorLabel,
            DominantColors = response.DominantColors,
            AutoClass = response.AutoClass,
            IsWaste = response.IsWaste,
            WasteReason = response.WasteReason,
            Rating = existingRating,
            PluginResults = response.Results,
            RawJson = response.RawJson
        };
    }
}
