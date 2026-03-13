using System.IO;
using System.Windows;
using System.Windows.Controls;
using PhotoSelector.App.ViewModels;

namespace PhotoSelector.App;

public partial class MainWindow
{
    private async Task EnterFolderPageAsync(string folderKey)
    {
        _selectedLibraryKey = folderKey;
        BackToGalleryButton.Visibility = Visibility.Visible;
        GalleryPage.Visibility = Visibility.Collapsed;
        FolderPage.Visibility = Visibility.Visible;
        SetFolderOperationEnabled(true);

        var selectedLibrary = _libraries.FirstOrDefault(x => x.Key.Equals(folderKey, StringComparison.OrdinalIgnoreCase));
        FolderTitleText.Text = selectedLibrary?.FolderPath ?? folderKey;

        RebuildGroups();
        ApplyFilters();
        UpdateThumbnailCardMetrics();

        var noThumb = GetLibraryScopedRows()
            .Select(r => r.Photo)
            .Where(p => string.IsNullOrWhiteSpace(p.ThumbnailPath) || !File.Exists(p.ThumbnailPath))
            .ToList();

        if (noThumb.Count == 0)
        {
            return;
        }

        var token = BeginOperation($"准备文件夹缩略图 ({noThumb.Count})");
        try
        {
            await EnsureThumbnailsAsync(noThumb, "准备缩略图", token);
            await SaveStateAsync(token);
            ApplyFilters();
            StatusText.Text = $"文件夹加载完成，共 {GetLibraryScopedRows().Count()} 张";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "文件夹加载已取消";
        }
        finally
        {
            EndOperation(StatusText.Text);
        }
    }

    private void SetFolderOperationEnabled(bool enabled)
    {
        AnalyzeSelectedButton.IsEnabled = enabled;
        AnalyzeAllButton.IsEnabled = enabled;
        ApplyRuleButton.IsEnabled = enabled;
        ExportButton.IsEnabled = enabled;
        SortComboBox.IsEnabled = enabled;
        ColumnsSlider.IsEnabled = enabled;
    }

    private IEnumerable<PhotoRow> GetLibraryScopedRows()
    {
        if (string.IsNullOrWhiteSpace(_selectedLibraryKey))
        {
            return Enumerable.Empty<PhotoRow>();
        }

        return _rows.Where(r => r.Photo.LibraryFolder.Equals(_selectedLibraryKey, StringComparison.OrdinalIgnoreCase));
    }

    private void RebuildGroups()
    {
        var scoped = GetLibraryScopedRows().ToList();
        var expandState = _groups.ToDictionary(n => n.Key, n => n.IsExpanded, StringComparer.OrdinalIgnoreCase);

        var styleValues = scoped
            .Where(r => r.IsAnalyzed && !string.IsNullOrWhiteSpace(r.StyleLabel))
            .Select(r => CanonicalGroup("style", r.StyleLabel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        var colorValues = scoped
            .Where(r => r.IsAnalyzed && !string.IsNullOrWhiteSpace(r.ColorLabel))
            .Select(r => CanonicalGroup("color", r.ColorLabel))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        var personValues = scoped
            .Where(r => r.IsAnalyzed && !string.IsNullOrWhiteSpace(r.PersonLabel) && !r.PersonLabel.Equals("none", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.PersonLabel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s)
            .ToList();

        _groups.Clear();
        _groups.Add(new GroupNode
        {
            Key = "all",
            Title = $"所有 ({scoped.Count})",
            IsExpanded = expandState.TryGetValue("all", out var exAll) ? exAll : true
        });

        var faceRoot = new GroupNode
        {
            Key = "root:face",
            Title = "人脸",
            IsExpanded = expandState.TryGetValue("root:face", out var exFace) ? exFace : true
        };
        faceRoot.Children.Add(new GroupNode { Key = "face:any", Title = $"有人脸 ({scoped.Count(r => r.FaceCount > 0)})" });
        faceRoot.Children.Add(new GroupNode { Key = "face:multi", Title = $"多人 ({scoped.Count(r => r.FaceCount > 1)})" });
        faceRoot.Children.Add(new GroupNode { Key = "face:none", Title = $"无人脸 ({scoped.Count(r => r.IsAnalyzed && r.FaceCount == 0)})" });
        foreach (var person in personValues)
        {
            var count = scoped.Count(r => r.PersonLabel.Equals(person, StringComparison.OrdinalIgnoreCase));
            faceRoot.Children.Add(new GroupNode { Key = $"person:{person}", Title = $"{GetPersonDisplayName(person)} ({count})" });
        }
        _groups.Add(faceRoot);

        var qualityRoot = new GroupNode
        {
            Key = "root:quality",
            Title = "质量",
            IsExpanded = expandState.TryGetValue("root:quality", out var exQuality) ? exQuality : false
        };
        qualityRoot.Children.Add(new GroupNode { Key = "waste:true", Title = $"废片 ({scoped.Count(r => r.IsWaste)})" });
        qualityRoot.Children.Add(new GroupNode { Key = "waste:false", Title = $"可用 ({scoped.Count(r => r.IsAnalyzed && !r.IsWaste)})" });
        qualityRoot.Children.Add(new GroupNode { Key = "unanalyzed", Title = $"未分析 ({scoped.Count(r => !r.IsAnalyzed)})" });
        qualityRoot.Children.Add(new GroupNode { Key = "top:10", Title = $"Top10% ({Math.Max(1, scoped.Count / 10)})" });
        _groups.Add(qualityRoot);

        var ratingRoot = new GroupNode
        {
            Key = "root:rating",
            Title = "星级",
            IsExpanded = expandState.TryGetValue("root:rating", out var exRating) ? exRating : false
        };
        ratingRoot.Children.Add(new GroupNode { Key = "rating:0", Title = $"未评分 ({scoped.Count(r => r.Rating <= 0)})" });
        for (var star = 1; star <= 5; star++)
        {
            var count = scoped.Count(r => r.Rating == star);
            ratingRoot.Children.Add(new GroupNode { Key = $"rating:{star}", Title = $"{star} 星 ({count})" });
        }
        _groups.Add(ratingRoot);

        var styleRoot = new GroupNode
        {
            Key = "root:style",
            Title = $"风格 ({styleValues.Count})",
            IsExpanded = expandState.TryGetValue("root:style", out var exStyle) ? exStyle : true
        };
        foreach (var style in styleValues)
        {
            var count = scoped.Count(r => CanonicalGroup("style", r.StyleLabel).Equals(style, StringComparison.OrdinalIgnoreCase));
            styleRoot.Children.Add(new GroupNode { Key = $"style:{style}", Title = $"{style} ({count})" });
        }
        _groups.Add(styleRoot);

        var colorRoot = new GroupNode
        {
            Key = "root:color",
            Title = $"颜色 ({colorValues.Count})",
            IsExpanded = expandState.TryGetValue("root:color", out var exColor) ? exColor : false
        };
        foreach (var color in colorValues)
        {
            var count = scoped.Count(r => CanonicalGroup("color", r.ColorLabel).Equals(color, StringComparison.OrdinalIgnoreCase));
            colorRoot.Children.Add(new GroupNode
            {
                Key = $"color:{color}",
                Title = $"{color} ({count})",
                ColorSwatch = ColorLabelToSwatch(color)
            });
        }
        _groups.Add(colorRoot);

        _selectedFilterKey = string.IsNullOrWhiteSpace(_selectedFilterKey) ? "all" : _selectedFilterKey;
        ColumnsText.Text = PhotoColumns.ToString();
    }

    private void ApplyFilters()
    {
        var key = _selectedFilterKey;
        var scoped = GetLibraryScopedRows();
        IEnumerable<PhotoRow> filtered = scoped;

        if (key == "all")
        {
            filtered = scoped;
        }
        else if (key == "unanalyzed")
        {
            filtered = scoped.Where(r => !r.IsAnalyzed);
        }
        else if (key.StartsWith("face:", StringComparison.OrdinalIgnoreCase))
        {
            var mode = key.Split(':')[1];
            filtered = mode switch
            {
                "any" => scoped.Where(r => r.FaceCount > 0),
                "multi" => scoped.Where(r => r.FaceCount > 1),
                "none" => scoped.Where(r => r.IsAnalyzed && r.FaceCount == 0),
                _ => scoped,
            };
        }
        else if (key.StartsWith("person:", StringComparison.OrdinalIgnoreCase))
        {
            var person = key["person:".Length..];
            filtered = scoped.Where(r => r.PersonLabel.Equals(person, StringComparison.OrdinalIgnoreCase));
        }
        else if (key.StartsWith("waste:", StringComparison.OrdinalIgnoreCase))
        {
            var flag = key.EndsWith("true", StringComparison.OrdinalIgnoreCase);
            filtered = scoped.Where(r => r.IsAnalyzed && r.IsWaste == flag);
        }
        else if (key.StartsWith("style:", StringComparison.OrdinalIgnoreCase))
        {
            var style = key["style:".Length..];
            filtered = scoped.Where(r => CanonicalGroup("style", r.StyleLabel).Equals(style, StringComparison.OrdinalIgnoreCase));
        }
        else if (key.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
        {
            var color = key["color:".Length..];
            filtered = scoped.Where(r => CanonicalGroup("color", r.ColorLabel).Equals(color, StringComparison.OrdinalIgnoreCase));
        }
        else if (key.StartsWith("rating:", StringComparison.OrdinalIgnoreCase))
        {
            var ratingText = key["rating:".Length..];
            if (int.TryParse(ratingText, out var rating))
            {
                filtered = rating <= 0
                    ? scoped.Where(r => r.Rating <= 0)
                    : scoped.Where(r => r.Rating == rating);
            }
        }
        else if (key.StartsWith("top:", StringComparison.OrdinalIgnoreCase))
        {
            var scopedList = scoped.ToList();
            var count = Math.Max(1, (int)Math.Ceiling(scopedList.Count * 0.1));
            filtered = scopedList.Where(r => r.IsAnalyzed).OrderByDescending(r => r.OverallScore).Take(count);
        }

        filtered = ApplySort(filtered);

        _visibleRows.Clear();
        foreach (var row in filtered)
        {
            _visibleRows.Add(row);
        }

        StatusText.Text = $"当前筛选: {key}, 数量={_visibleRows.Count}";
    }

    private IEnumerable<PhotoRow> ApplySort(IEnumerable<PhotoRow> rows)
    {
        return _sortKey switch
        {
            "overall_asc" => rows.OrderBy(r => r.OverallScore),
            "sharpness_desc" => rows.OrderByDescending(r => r.Sharpness),
            "exposure_desc" => rows.OrderByDescending(r => r.Photo.Analysis.ExposureScore),
            "face_desc" => rows.OrderByDescending(r => r.FaceCount),
            "rating_desc" => rows.OrderByDescending(r => r.Rating).ThenByDescending(r => r.OverallScore),
            "name_asc" => rows.OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase),
            _ => rows.OrderByDescending(r => r.OverallScore),
        };
    }

    private void GroupTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not GroupNode node)
        {
            return;
        }

        if (!node.IsLeaf)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        _selectedFilterKey = node.Key;
        ApplyFilters();
    }

    private void FolderPage_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateThumbnailCardMetrics();
    }

    private void ColumnsSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ColumnsText is not null)
        {
            var value = Math.Clamp((int)Math.Round(ColumnsSlider.Value), 1, 10);
            PhotoColumns = value;
            ColumnsText.Text = value.ToString();
            UpdateThumbnailCardMetrics();
        }
    }

    private void SortComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string key)
        {
            _sortKey = key;
            if (FolderPage.Visibility == Visibility.Visible)
            {
                ApplyFilters();
            }
        }
    }

    private void GroupList_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (GroupList.ContextMenu is null)
        {
            return;
        }

        if ((GroupList.SelectedItem as GroupNode) is not GroupNode selected || !selected.IsLeaf)
        {
            GroupList.ContextMenu.Items.Clear();
            GroupList.ContextMenu.Items.Add(new MenuItem { Header = "请选择叶子分类", IsEnabled = false });
            return;
        }

        GroupList.ContextMenu.Items.Clear();

        if (selected.Key.StartsWith("person:", StringComparison.OrdinalIgnoreCase))
        {
            var personId = selected.Key["person:".Length..];
            var rename = new MenuItem { Header = "重命名人物", Tag = personId };
            rename.Click += RenamePerson_OnClick;
            GroupList.ContextMenu.Items.Add(rename);
            return;
        }

        if (!selected.Key.StartsWith("style:", StringComparison.OrdinalIgnoreCase)
            && !selected.Key.StartsWith("color:", StringComparison.OrdinalIgnoreCase))
        {
            GroupList.ContextMenu.Items.Add(new MenuItem { Header = "该项不支持归档", IsEnabled = false });
            return;
        }

        var prefix = selected.Key.StartsWith("style:", StringComparison.OrdinalIgnoreCase) ? "style" : "color";
        var source = selected.Key[(prefix.Length + 1)..].Split(' ')[0];

        var targetKeys = _groups
            .SelectMany(r => r.Children)
            .Where(c => c.IsLeaf && c.Key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Key[(prefix.Length + 1)..].Split(' ')[0])
            .Where(k => !k.Equals(source, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        if (targetKeys.Count == 0)
        {
            GroupList.ContextMenu.Items.Add(new MenuItem { Header = "无可归档目标", IsEnabled = false });
            return;
        }

        var parent = new MenuItem { Header = $"归档 {source} 到..." };
        foreach (var target in targetKeys)
        {
            var item = new MenuItem { Header = target, Tag = $"{prefix}|{source}|{target}" };
            item.Click += ArchiveGroupTo_OnClick;
            parent.Items.Add(item);
        }

        GroupList.ContextMenu.Items.Add(parent);
    }

    private async void RenamePerson_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string personId)
        {
            return;
        }

        var current = GetPersonDisplayName(personId);
        var input = Microsoft.VisualBasic.Interaction.InputBox("请输入人物名称", "重命名人物", current);
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        _personNames[personId] = input.Trim();
        foreach (var row in _rows.Where(r => r.PersonLabel.Equals(personId, StringComparison.OrdinalIgnoreCase)))
        {
            row.Refresh();
        }

        await SavePersonNamesAsync();
        RebuildGroups();
        ApplyFilters();
        StatusText.Text = $"人物已重命名: {current} -> {input.Trim()}";
    }

    private async void ArchiveGroupTo_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string payload)
        {
            return;
        }

        var parts = payload.Split('|');
        if (parts.Length != 3)
        {
            return;
        }

        var prefix = parts[0];
        var source = parts[1];
        var target = parts[2];

        _classArchiveMap[$"{prefix}:{source}"] = $"{prefix}:{target}";
        foreach (var row in _rows)
        {
            if (prefix == "style" && CanonicalGroup("style", row.StyleLabel).Equals(source, StringComparison.OrdinalIgnoreCase))
            {
                row.Photo.Analysis.StyleLabel = target;
                row.Refresh();
            }

            if (prefix == "color" && CanonicalGroup("color", row.ColorLabel).Equals(source, StringComparison.OrdinalIgnoreCase))
            {
                row.Photo.Analysis.ColorLabel = target;
                row.Refresh();
            }
        }

        await SaveClassArchiveMapAsync();
        await SaveStateAsync();
        RebuildGroups();
        ApplyFilters();
        StatusText.Text = $"已归档: {prefix}/{source} -> {target}";
    }

    private string CanonicalGroup(string prefix, string value)
    {
        var key = $"{prefix}:{value}";
        var loop = 0;
        while (_classArchiveMap.TryGetValue(key, out var next) && !string.IsNullOrWhiteSpace(next))
        {
            key = next;
            loop++;
            if (loop > 12)
            {
                break;
            }
        }

        return key.Contains(':') ? key.Split(':', 2)[1] : value;
    }

    private static string ColorLabelToSwatch(string label)
    {
        return label.ToLowerInvariant() switch
        {
            "red" => "#E74C3C",
            "orange" => "#E67E22",
            "yellow" => "#F1C40F",
            "green" => "#2ECC71",
            "cyan" => "#1ABC9C",
            "blue" => "#3498DB",
            "purple" => "#9B59B6",
            "magenta" => "#D252D8",
            "gray" => "#95A5A6",
            "black" => "#2D3436",
            _ => "#7F8C8D",
        };
    }
}
