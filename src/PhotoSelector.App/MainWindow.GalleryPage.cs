using System.IO;
using System.Windows;
using System.Windows.Controls;
using PhotoSelector.App.ViewModels;

namespace PhotoSelector.App;

public partial class MainWindow
{
    private void RebuildLibraries()
    {
        var previous = _selectedLibraryKey;
        var groups = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Photo.LibraryFolder))
            .GroupBy(r => r.Photo.LibraryFolder, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key)
            .ToList();

        _libraries.Clear();
        foreach (var folderGroup in groups)
        {
            var cover = folderGroup
                .Select(x => string.IsNullOrWhiteSpace(x.Photo.ThumbnailPath) ? x.Path : x.Photo.ThumbnailPath)
                .FirstOrDefault(p => File.Exists(p)) ?? string.Empty;

            _libraries.Add(new LibraryFolderRow
            {
                Key = folderGroup.Key,
                FolderPath = folderGroup.Key,
                PhotoCount = folderGroup.Count(),
                CoverPath = cover,
                Title = $"{Path.GetFileName(folderGroup.Key)} ({folderGroup.Count()})"
            });
        }

        if (string.IsNullOrWhiteSpace(previous) && _libraries.Count > 0)
        {
            _selectedLibraryKey = _libraries[0].Key;
        }
        else if (!string.IsNullOrWhiteSpace(previous) && _libraries.All(x => !x.Key.Equals(previous, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedLibraryKey = _libraries.FirstOrDefault()?.Key;
        }
    }

    private void EnterGalleryPage()
    {
        BackToGalleryButton.Visibility = Visibility.Collapsed;
        GalleryPage.Visibility = Visibility.Visible;
        FolderPage.Visibility = Visibility.Collapsed;
        SetFolderOperationEnabled(false);
    }

    private void BackToGalleryButton_OnClick(object sender, RoutedEventArgs e)
    {
        EnterGalleryPage();
    }
}
