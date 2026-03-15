using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using PhotoSelector.App.Services;
using PhotoSelector.App.ViewModels;
using PhotoSelector.Application.Interfaces;
using PhotoSelector.Application.Services;
using PhotoSelector.Domain.Models;
using PhotoSelector.Infrastructure.Persistence;
using PhotoSelector.Infrastructure.Services;
using Forms = System.Windows.Forms;

namespace PhotoSelector.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<PhotoRow> _rows = new();
    private readonly ObservableCollection<PhotoRow> _visibleRows = new();
    private readonly ObservableCollection<GroupNode> _groups = new();
    private readonly ObservableCollection<LibraryFolderRow> _libraries = new();
    private readonly List<PhotoItem> _photos = new();
    private readonly PhotoImportService _importService;
    private readonly AnalysisCoordinator _analysisCoordinator;
    private readonly IRuleEngine _ruleEngine = new RuleEngineService();
    private readonly IPhotoRepository _photoRepository;
    private readonly HttpClient _httpClient;
    private readonly ThumbnailCacheService _thumbnailCache;
    private readonly string _classArchivePath;
    private readonly string _personNamesPath;
    private readonly Dictionary<string, string> _classArchiveMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _personNames = new(StringComparer.OrdinalIgnoreCase);

    private Process? _aiProcess;
    private string? _selectedLibraryKey;
    private string _selectedFilterKey = "all";
    private CancellationTokenSource? _operationCts;
    private string _sortKey = "overall_desc";

    public static readonly DependencyProperty PhotoCardWidthProperty = DependencyProperty.Register(
        nameof(PhotoCardWidth), typeof(double), typeof(MainWindow), new PropertyMetadata(220d));

    public static readonly DependencyProperty PhotoThumbHeightProperty = DependencyProperty.Register(
        nameof(PhotoThumbHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(140d));
    public static readonly DependencyProperty PhotoColumnsProperty = DependencyProperty.Register(
        nameof(PhotoColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(5));
    public static readonly DependencyProperty PhotoCardHeightProperty = DependencyProperty.Register(
        nameof(PhotoCardHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(260d));

    public double PhotoCardWidth
    {
        get => (double)GetValue(PhotoCardWidthProperty);
        set => SetValue(PhotoCardWidthProperty, value);
    }

    public double PhotoThumbHeight
    {
        get => (double)GetValue(PhotoThumbHeightProperty);
        set => SetValue(PhotoThumbHeightProperty, value);
    }

    public int PhotoColumns
    {
        get => (int)GetValue(PhotoColumnsProperty);
        set => SetValue(PhotoColumnsProperty, value);
    }

    public double PhotoCardHeight
    {
        get => (double)GetValue(PhotoCardHeightProperty);
        set => SetValue(PhotoCardHeightProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:8000")
        };

        IAiServiceClient aiServiceClient = new HttpAiServiceClient(_httpClient);
        _analysisCoordinator = new AnalysisCoordinator(aiServiceClient);
        _importService = new PhotoImportService(new ExifMetadataReader());

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var dbPath = Path.Combine(dataDir, "photos.db");
        _thumbnailCache = new ThumbnailCacheService(Path.Combine(dataDir, "thumbs"));
        _classArchivePath = Path.Combine(dataDir, "class_archives.json");
        _personNamesPath = Path.Combine(dataDir, "person_names.json");
        _photoRepository = new SqlitePhotoRepository(dbPath);

        PhotoRow.PersonNameResolver = GetPersonDisplayName;

        PhotoList.ItemsSource = _visibleRows;
        GroupList.ItemsSource = _groups;
        GalleryFolderList.ItemsSource = _libraries;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BeginLoadingRotation();
        await InitializeAsync();
    }

    private void BeginLoadingRotation()
    {
        var rotate = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.2))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        LoadingRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, rotate);
    }

    private async Task InitializeAsync()
    {
        try
        {
            await EnsureAiServiceAsync();
            await LoadClassArchiveMapAsync();
            await LoadPersonNamesAsync();
            await LoadStateAsync();
            RebuildLibraries();
            EnterGalleryPage();
            StatusText.Text = $"已加载 {_rows.Count} 张照片";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"初始化失败: {ex.Message}";
        }
        finally
        {
            await PlayStartupExitAnimationAsync();
        }
    }

    private async Task PlayStartupExitAnimationAsync()
    {
        await Dispatcher.InvokeAsync(() =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280));
            MainContentRoot.BeginAnimation(OpacityProperty, fadeIn);

            var startupFade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(380));
            startupFade.Completed += (_, _) => StartupOverlay.Visibility = Visibility.Collapsed;
            StartupOverlay.BeginAnimation(OpacityProperty, startupFade);
        });
    }

    private async Task EnsureAiServiceAsync()
    {
        if (await IsAiServiceHealthyAsync())
        {
            return;
        }

        var serviceDir = FindAiServiceDirectory();
        if (serviceDir is null)
        {
            throw new InvalidOperationException("未找到 python-ai-service 目录，无法自动启动 AI 服务。");
        }

        _aiProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "python",
            Arguments = "-m uvicorn main:app --host 127.0.0.1 --port 8000",
            WorkingDirectory = serviceDir,
            CreateNoWindow = true,
            UseShellExecute = false
        });

        for (var i = 0; i < 20; i++)
        {
            if (await IsAiServiceHealthyAsync())
            {
                return;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException("AI 服务启动超时。");
    }

    private async Task<bool> IsAiServiceHealthyAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync("/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindAiServiceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "python-ai-service", "main.py");
            if (File.Exists(candidate))
            {
                return Path.Combine(dir.FullName, "python-ai-service");
            }

            dir = dir.Parent;
        }

        return null;
    }

    private async Task LoadStateAsync()
    {
        var loaded = await _photoRepository.LoadAsync();
        _photos.Clear();
        _rows.Clear();
        _visibleRows.Clear();
        foreach (var photo in loaded)
        {
            if (string.IsNullOrWhiteSpace(photo.LibraryFolder))
            {
                photo.LibraryFolder = Path.GetDirectoryName(photo.Path) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(photo.ThumbnailPath))
            {
                var candidate = _thumbnailCache.GetThumbnailPath(photo.Path, photo.LibraryFolder);
                if (File.Exists(candidate))
                {
                    photo.ThumbnailPath = candidate;
                }
            }

            var row = new PhotoRow(photo);
            photo.Analysis.StyleLabel = CanonicalGroup("style", photo.Analysis.StyleLabel);
            photo.Analysis.ColorLabel = CanonicalGroup("color", photo.Analysis.ColorLabel);
            photo.Analysis.AutoClass = CanonicalClass(photo.Analysis.AutoClass);
            _photos.Add(photo);
            _rows.Add(row);
        }
    }

    private Task SaveStateAsync(CancellationToken cancellationToken = default)
        => _photoRepository.SaveAsync(_photos, cancellationToken);

    private async Task LoadClassArchiveMapAsync()
    {
        _classArchiveMap.Clear();
        if (!File.Exists(_classArchivePath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_classArchivePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null)
            {
                return;
            }

            foreach (var pair in dict)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    _classArchiveMap[pair.Key] = pair.Value;
                }
            }
        }
        catch
        {
            // Ignore malformed archive file.
        }
    }

    private async Task SaveClassArchiveMapAsync()
    {
        var json = JsonSerializer.Serialize(_classArchiveMap, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_classArchivePath, json);
    }

    private async Task LoadPersonNamesAsync()
    {
        _personNames.Clear();
        if (!File.Exists(_personNamesPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_personNamesPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null)
            {
                return;
            }

            foreach (var pair in dict)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    _personNames[pair.Key] = pair.Value;
                }
            }
        }
        catch
        {
            // Ignore malformed person names.
        }
    }

    private async Task SavePersonNamesAsync()
    {
        var json = JsonSerializer.Serialize(_personNames, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_personNamesPath, json);
    }

    private string GetPersonDisplayName(string label)
    {
        if (_personNames.TryGetValue(label, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        if (label.StartsWith("person_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(label["person_".Length..], out var idx))
        {
            return $"人物{idx}";
        }

        return label;
    }

    private string CanonicalClass(string input)
    {
        var value = input;
        var loopGuard = 0;
        while (_classArchiveMap.TryGetValue(value, out var next) && !string.IsNullOrWhiteSpace(next))
        {
            value = next;
            loopGuard++;
            if (loopGuard > 12)
            {
                break;
            }
        }

        return value;
    }

    private void UpdateThumbnailCardMetrics()
    {
        if (PhotoList is null || ColumnsSlider is null)
        {
            return;
        }

        var columns = Math.Max(1, (int)Math.Round(ColumnsSlider.Value));
        PhotoColumns = columns;
        var totalWidth = Math.Max(360, PhotoList.ActualWidth - 20);
        var gap = 10d;
        var width = (totalWidth - gap * (columns - 1)) / columns;
        width = Math.Clamp(width, 76, 360);
        PhotoCardWidth = width;
        PhotoThumbHeight = Math.Clamp(width * 0.68, 64, 250);
        PhotoCardHeight = PhotoThumbHeight + 120;
    }

    private async Task SubmitFeedbackAsync(PhotoRow row, string manualTag, string note)
    {
        try
        {
            var payload = new
            {
                image_path = row.Path,
                manual_tag = manualTag,
                note,
                predicted_is_waste = row.Photo.Analysis.IsWaste,
                predicted_style = row.Photo.Analysis.StyleLabel,
                predicted_face_count = row.Photo.Analysis.FaceCount
            };
            await _httpClient.PostAsJsonAsync("/feedback", payload);
        }
        catch
        {
            // Best effort.
        }
    }

    private async Task SubmitPersonRenameAsync(string oldLabel, string newLabel)
    {
        try
        {
            var payload = new { old_label = oldLabel, new_label = newLabel };
            await _httpClient.PostAsJsonAsync("/person/rename", payload);
        }
        catch
        {
            // Best effort.
        }
    }

    private async Task SubmitPersonAssignAsync(string imagePath, string newLabel)
    {
        try
        {
            var payload = new { image_path = imagePath, new_label = newLabel };
            await _httpClient.PostAsJsonAsync("/person/assign", payload);
        }
        catch
        {
            // Best effort.
        }
    }

    private CancellationToken BeginOperation(string message, bool canCancel = true)
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        OperationText.Text = message;
        CancelOperationButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        OperationOverlay.Visibility = Visibility.Visible;
        SetToolbarEnabled(false);
        return _operationCts.Token;
    }

    private void EndOperation(string status)
    {
        OperationOverlay.Visibility = Visibility.Collapsed;
        SetToolbarEnabled(true);
        SetFolderOperationEnabled(FolderPage.Visibility == Visibility.Visible);
        StatusText.Text = status;
    }

    private void SetToolbarEnabled(bool enabled)
    {
        BackToGalleryButton.IsEnabled = enabled;
        ImportButton.IsEnabled = enabled;
        AnalyzeSelectedButton.IsEnabled = enabled;
        AnalyzeAllButton.IsEnabled = enabled;
        ApplyRuleButton.IsEnabled = enabled;
        ExportButton.IsEnabled = enabled;
    }

    private void CancelOperationButton_OnClick(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        OperationText.Text = "取消中...";
    }

    private static PhotoRow? GetRowFromMenu(object sender)
        => (sender as MenuItem)?.CommandParameter as PhotoRow;

    private void OnClosed(object? sender, EventArgs e)
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _httpClient.Dispose();
        if (_aiProcess is { HasExited: false })
        {
            try
            {
                _aiProcess.Kill(true);
            }
            catch
            {
                // Ignore cleanup failure.
            }
        }
    }
}
