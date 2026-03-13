using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoSelector.App.ViewModels;
using PhotoSelector.Domain.Models;
using PhotoSelector.Domain.Rules;
using Forms = System.Windows.Forms;

namespace PhotoSelector.App;

public partial class MainWindow
{
    private async Task OpenFolderFromRowAsync(LibraryFolderRow row)
    {
        if (row is null)
        {
            return;
        }

        await EnterFolderPageAsync(row.Key);
    }

    private async Task RemoveFolderAsync(LibraryFolderRow row, bool deleteDisk)
    {
        if (row is null)
        {
            return;
        }

        var confirmText = deleteDisk
            ? $"确认删除磁盘文件夹？\n{row.FolderPath}\n该操作会删除文件夹及其中图片。"
            : $"确认从图库移除文件夹？\n{row.FolderPath}\n不会删除磁盘原文件。";
        var confirm = System.Windows.MessageBox.Show(
            confirmText,
            "确认操作",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var token = BeginOperation(deleteDisk ? "删除文件夹中..." : "从图库移除中...");
        try
        {
            var targetRows = _rows
                .Where(r => r.Photo.LibraryFolder.Equals(row.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var targetRow in targetRows)
            {
                token.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(targetRow.Photo.ThumbnailPath) && File.Exists(targetRow.Photo.ThumbnailPath))
                {
                    try
                    {
                        File.Delete(targetRow.Photo.ThumbnailPath);
                    }
                    catch
                    {
                        // Ignore thumbnail cleanup failures.
                    }
                }

                _rows.Remove(targetRow);
                _photos.Remove(targetRow.Photo);
            }

            _visibleRows.Clear();

            if (deleteDisk && Directory.Exists(row.FolderPath))
            {
                Directory.Delete(row.FolderPath, true);
            }

            if (_selectedLibraryKey?.Equals(row.Key, StringComparison.OrdinalIgnoreCase) == true)
            {
                _selectedLibraryKey = null;
            }

            await SaveStateAsync(token);
            RebuildLibraries();
            EnterGalleryPage();
            StatusText.Text = deleteDisk ? "磁盘文件夹已删除" : "已从图库移除";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "操作已取消";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"操作失败: {ex.Message}";
        }
        finally
        {
            EndOperation(StatusText.Text);
        }
    }

    private async void OpenLibraryFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not LibraryFolderRow row)
        {
            return;
        }

        await OpenFolderFromRowAsync(row);
    }

    private async void OpenLibraryFolderFromMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not LibraryFolderRow row)
        {
            return;
        }

        await OpenFolderFromRowAsync(row);
    }

    private async void RemoveLibraryFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not LibraryFolderRow row)
        {
            return;
        }

        await RemoveFolderAsync(row, deleteDisk: false);
    }

    private async void DeleteLibraryFolderDisk_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.CommandParameter is not LibraryFolderRow row)
        {
            return;
        }

        await RemoveFolderAsync(row, deleteDisk: true);
    }

    private async void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return;
        }

        await ImportFolderAsync(dialog.SelectedPath);
    }

    private async void GalleryPage_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        var dropped = (string[]?)e.Data.GetData(System.Windows.DataFormats.FileDrop) ?? Array.Empty<string>();
        var folders = dropped.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count() == 0)
        {
            return;
        }

        foreach (var folder in folders)
        {
            await ImportFolderAsync(folder);
        }
    }

    private void GalleryPage_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async Task ImportFolderAsync(string folder)
    {
        var token = BeginOperation($"导入 {Path.GetFileName(folder)} ...");
        try
        {
            var imported = await Task.Run(() => _importService.ImportFromDirectory(folder, recursive: true), token);
            var newItems = new List<PhotoItem>();
            foreach (var item in imported)
            {
                token.ThrowIfCancellationRequested();
                if (_photos.Any(p => p.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                item.LibraryFolder = Path.GetFullPath(folder);
                _photos.Add(item);
                _rows.Add(new PhotoRow(item));
                newItems.Add(item);
            }

            await EnsureThumbnailsAsync(newItems, "生成缩略图中", token);
            await SaveStateAsync(token);
            RebuildLibraries();
            StatusText.Text = $"导入完成: {Path.GetFileName(folder)}，新增 {newItems.Count} 张";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "导入已取消";
        }
        finally
        {
            EndOperation(StatusText.Text);
        }
    }

    private async Task EnsureThumbnailsAsync(IReadOnlyCollection<PhotoItem> photos, string stepMessage, CancellationToken token)
    {
        if (photos.Count == 0)
        {
            return;
        }

        var list = photos.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var photo = list[i];
            OperationText.Text = $"{stepMessage} {i + 1}/{list.Count}";
            if (!File.Exists(photo.Path))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(photo.ThumbnailPath) || !File.Exists(photo.ThumbnailPath))
            {
                photo.ThumbnailPath = await _thumbnailCache.EnsureThumbnailAsync(
                    photo.Path,
                    960,
                    photo.LibraryFolder,
                    token);
                var row = _rows.FirstOrDefault(r => r.Photo.Id == photo.Id);
                row?.Refresh();
            }
        }
    }

    private async void AnalyzeSelectedButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (PhotoList.SelectedItem is not PhotoRow row)
        {
            StatusText.Text = "请先选择一张照片";
            return;
        }

        await AnalyzeRowsAsync(new[] { row });
    }

    private async void AnalyzeAllButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Count == 0)
        {
            StatusText.Text = "当前筛选下没有可分析照片";
            return;
        }

        await AnalyzeRowsAsync(_visibleRows.ToList());
    }

    private async Task AnalyzeRowsAsync(IReadOnlyCollection<PhotoRow> rows)
    {
        var token = BeginOperation("AI分析中...");
        try
        {
            var photos = rows.Select(r => r.Photo).ToList();
            await EnsureThumbnailsAsync(photos, "准备缩略图", token);

            const int batchSize = 12;
            for (var start = 0; start < rows.Count; start += batchSize)
            {
                token.ThrowIfCancellationRequested();
                var batch = rows.Skip(start).Take(batchSize).ToList();
                OperationText.Text = $"分析中 {Math.Min(start + batch.Count, rows.Count)}/{rows.Count}";

                await _analysisCoordinator.AnalyzeBatchAsync(
                    batch.Select(r => r.Photo).ToList(),
                    p => string.IsNullOrWhiteSpace(p.ThumbnailPath) ? p.Path : p.ThumbnailPath,
                    token);

                foreach (var row in batch)
                {
                    row.Photo.Analysis.StyleLabel = CanonicalGroup("style", row.Photo.Analysis.StyleLabel);
                    row.Photo.Analysis.ColorLabel = CanonicalGroup("color", row.Photo.Analysis.ColorLabel);
                    row.Photo.Analysis.AutoClass = CanonicalClass(row.Photo.Analysis.AutoClass);
                    row.Refresh();
                }
            }

            await SaveStateAsync(token);
            RebuildLibraries();
            RebuildGroups();
            ApplyFilters();
            StatusText.Text = $"分析完成，共 {rows.Count} 张";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "分析已取消";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"分析失败: {ex.Message}";
        }
        finally
        {
            EndOperation(StatusText.Text);
        }
    }

    private void PhotoList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PhotoList.SelectedItem is not PhotoRow row)
        {
            PreviewImage.Source = null;
            DetailOverallText.Text = string.Empty;
            DetailSharpnessText.Text = string.Empty;
            DetailExposureText.Text = string.Empty;
            DetailFaceText.Text = string.Empty;
            DetailClassText.Text = string.Empty;
            DetailWasteText.Text = string.Empty;
            DetailRawJsonText.Text = string.Empty;
            return;
        }

        try
        {
            var previewPath = string.IsNullOrWhiteSpace(row.Photo.ThumbnailPath) ? row.Path : row.Photo.ThumbnailPath;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(previewPath, UriKind.Absolute);
            bitmap.EndInit();
            PreviewImage.Source = bitmap;
        }
        catch
        {
            PreviewImage.Source = null;
        }

        DetailOverallText.Text = $"总分: {row.OverallScore:F3}";
        DetailSharpnessText.Text = $"清晰度: {row.Sharpness:F3}";
        DetailExposureText.Text = $"曝光: {row.Photo.Analysis.ExposureScore:F3}";
        DetailFaceText.Text = $"人像计数: {row.FaceCount} / 人物: {GetPersonDisplayName(row.PersonLabel)}";
        DetailClassText.Text = $"分类: {row.AutoClass} / 风格: {row.StyleLabel} / 颜色: {row.ColorLabel}";
        DetailWasteText.Text = row.IsWaste ? $"废片: {row.WasteReason}" : "废片: 否";
        DetailRawJsonText.Text = string.IsNullOrWhiteSpace(row.Photo.Analysis.RawJson)
            ? JsonSerializer.Serialize(row.Photo, new JsonSerializerOptions { WriteIndented = true })
            : row.Photo.Analysis.RawJson;
    }

    private void ApplyRuleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var expression = RuleTextBox.Text;
        if (string.IsNullOrWhiteSpace(expression))
        {
            StatusText.Text = "规则不能为空";
            return;
        }

        var scoped = GetLibraryScopedRows().ToList();
        var passing = new List<PhotoRow>();
        foreach (var row in scoped)
        {
            var result = _ruleEngine.Evaluate(expression, new RuleContext { Photo = row.Photo }, "QuickRule");
            if (result.Passed)
            {
                passing.Add(row);
            }
        }

        _visibleRows.Clear();
        foreach (var row in passing)
        {
            _visibleRows.Add(row);
        }

        StatusText.Text = $"规则通过: {passing.Count}/{scoped.Count}";
    }

    private async void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出筛选结果",
            Filter = "JSON|*.json",
            FileName = $"selection_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var payload = _visibleRows
            .Where(p => p.IsAnalyzed)
            .OrderByDescending(p => p.OverallScore)
            .Select(row => new
            {
                row.Photo.Id,
                row.Photo.Path,
                row.Photo.ThumbnailPath,
                row.Photo.FileName,
                row.Photo.LibraryFolder,
                row.Photo.Analysis.OverallScore,
                row.Photo.Analysis.SharpnessScore,
                row.Photo.Analysis.ExposureScore,
                row.Photo.Analysis.FaceCount,
                row.Photo.Analysis.StyleLabel,
                row.Photo.Analysis.IsWaste,
                row.Photo.Analysis.WasteReason,
                row.Photo.Analysis.EyesClosed,
                row.Photo.Analysis.IsDuplicate
            })
            .ToList();

        await File.WriteAllTextAsync(
            dialog.FileName,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);

        StatusText.Text = $"已导出: {dialog.FileName}";
    }

    private async void ManualTagGood_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetRowFromMenu(sender) is not { } row) return;
        row.Photo.Analysis.IsWaste = false;
        row.Photo.Analysis.WasteReason = "manual_good";
        row.Refresh();
        await SubmitFeedbackAsync(row, "good", "manual tag good");
        await SaveStateAsync();
        RebuildGroups();
        ApplyFilters();
    }

    private async void ManualTagWaste_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetRowFromMenu(sender) is not { } row) return;
        row.Photo.Analysis.IsWaste = true;
        row.Photo.Analysis.WasteReason = "manual_waste";
        row.Refresh();
        await SubmitFeedbackAsync(row, "waste", "manual tag waste");
        await SaveStateAsync();
        RebuildGroups();
        ApplyFilters();
    }

    private async void ManualTagPortrait_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetRowFromMenu(sender) is not { } row) return;
        row.Photo.Analysis.FaceCount = Math.Max(1, row.Photo.Analysis.FaceCount);
        row.Refresh();
        await SubmitFeedbackAsync(row, "portrait", "manual tag portrait");
        await SaveStateAsync();
        RebuildGroups();
        ApplyFilters();
    }

    private async void SetRating_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button
            || button.CommandParameter is not PhotoRow row
            || button.Tag is not string tag
            || !int.TryParse(tag, out var rating))
        {
            return;
        }

        row.Photo.Analysis.Rating = Math.Clamp(rating, 1, 5);
        row.Refresh();
        await SaveStateAsync();
        RebuildGroups();
        ApplyFilters();
    }

    private async void Reanalyze_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetRowFromMenu(sender) is not { } row) return;
        await AnalyzeRowsAsync(new[] { row });
    }

    private void OpenRowMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is not ContextMenu menu)
        {
            return;
        }

        menu.Placement = PlacementMode.Bottom;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void OpenLibraryMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.ContextMenu is not ContextMenu menu)
        {
            return;
        }

        menu.Placement = PlacementMode.Bottom;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void FullscreenPreview_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.CommandParameter is not PhotoRow row)
        {
            return;
        }

        var currentRows = _visibleRows.ToList();
        var startIndex = currentRows.FindIndex(r => r.Photo.Id == row.Photo.Id);
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        var preview = new PreviewWindow(currentRows, startIndex, (target, rating) =>
        {
            target.Photo.Analysis.Rating = Math.Clamp(rating, 1, 5);
            target.Refresh();
            RebuildGroups();
            ApplyFilters();
            _ = SaveStateAsync();
        })
        { Owner = this };
        preview.ShowDialog();
    }
}
