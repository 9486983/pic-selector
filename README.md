# PhotoSelectorV3

PhotoSelectorV3 is a WPF-based photo curation app with a local AI service for analysis (face detection, style, color, quality) and local persistence.

Top-level
- `PhotoSelectorV3.sln`: Solution file.
- `role.md`: Project notes.
- `python-ai-service/`: FastAPI service used by the app for AI analysis.
- `RESULT-F60/`: Sample image set used for testing.
- `src/`: .NET projects for app, domain, infrastructure, and smoke test.
- `.gitignore`: Git ignore rules.

python-ai-service
- `python-ai-service/main.py`: FastAPI entrypoint and API routes.
- `python-ai-service/appsettings.json`: Service configuration (workers, thresholds).
- `python-ai-service/requirements.txt`: Python dependencies.
- `python-ai-service/yolov8n.pt`: YOLO model weights.
- `python-ai-service/core/engine.py`: Core analysis pipeline, scoring, learning state, and identity memory.
- `python-ai-service/core/plugin_base.py`: Plugin interface contract.
- `python-ai-service/core/scheduler.py`: GPU/CPU scheduler for plugin execution.
- `python-ai-service/plugins/yolo_plugin.py`: Object/person detection and face signature extraction.
- `python-ai-service/plugins/curation_plugin.py`: Style/brightness/color analysis.
- `python-ai-service/data/learning_state.json`: Persisted AI learning state (face identities, mappings).

src/PhotoSelector.App (WPF client)
- `src/PhotoSelector.App/App.xaml`: WPF app resources and startup.
- `src/PhotoSelector.App/MainWindow.xaml`: Main UI layout.
- `src/PhotoSelector.App/MainWindow.xaml.cs`: App initialization, service wiring, shared helpers.
- `src/PhotoSelector.App/MainWindow.Actions.cs`: UI actions (import, analyze, tagging, menus).
- `src/PhotoSelector.App/MainWindow.FolderPage.cs`: Folder page logic (filters, grouping).
- `src/PhotoSelector.App/MainWindow.GalleryPage.cs`: Gallery page logic.
- `src/PhotoSelector.App/PreviewWindow.xaml`: Fullscreen preview UI.
- `src/PhotoSelector.App/PreviewWindow.xaml.cs`: Fullscreen preview logic (zoom/pan/nav).
- `src/PhotoSelector.App/Converters/RatingStarConverter.cs`: Rating star display converter.
- `src/PhotoSelector.App/Converters/HexToBrushConverter.cs`: Hex color to brush converter.
- `src/PhotoSelector.App/Converters/StringHasValueToVisibilityConverter.cs`: Visibility converter for optional swatches.
- `src/PhotoSelector.App/Services/ThumbnailCacheService.cs`: Thumbnail generation and caching.
- `src/PhotoSelector.App/ViewModels/PhotoRow.cs`: UI row model for thumbnails.
- `src/PhotoSelector.App/ViewModels/GroupNode.cs`: UI tree group model.
- `src/PhotoSelector.App/Assets/loading.gif`: Startup loading animation.
- `src/PhotoSelector.App/PhotoSelector.App.csproj`: WPF project file.

src/PhotoSelector.Domain
- `src/PhotoSelector.Domain/Models/PhotoItem.cs`: Core photo record.
- `src/PhotoSelector.Domain/Models/PhotoMetadata.cs`: EXIF metadata fields.
- `src/PhotoSelector.Domain/Models/AnalysisAggregate.cs`: AI analysis results.
- `src/PhotoSelector.Domain/Models/DetectedObject.cs`: Detected object result.
- `src/PhotoSelector.Domain/Models/PluginResult.cs`: Plugin result structure.
- `src/PhotoSelector.Domain/Rules/*`: Rule engine domain types.
- `src/PhotoSelector.Domain/PhotoSelector.Domain.csproj`: Domain project file.

src/PhotoSelector.Application
- `src/PhotoSelector.Application/Interfaces/IAiServiceClient.cs`: AI client abstraction.
- `src/PhotoSelector.Application/Services/AnalysisCoordinator.cs`: Applies AI results to photos.
- `src/PhotoSelector.Application/PhotoSelector.Application.csproj`: Application project file.

src/PhotoSelector.Infrastructure
- `src/PhotoSelector.Infrastructure/Services/HttpAiServiceClient.cs`: HTTP client for AI service.
- `src/PhotoSelector.Infrastructure/Services/ExifMetadataReader.cs`: EXIF reader.
- `src/PhotoSelector.Infrastructure/Persistence/SqlitePhotoRepository.cs`: SQLite persistence.
- `src/PhotoSelector.Infrastructure/Persistence/sqlite_schema.sql`: SQLite schema.
- `src/PhotoSelector.Infrastructure/Persistence/JsonPhotoRepository.cs`: JSON persistence (legacy/alt).
- `src/PhotoSelector.Infrastructure/PhotoSelector.Infrastructure.csproj`: Infrastructure project file.

src/PhotoSelector.SmokeTest
- `src/PhotoSelector.SmokeTest/Program.cs`: Smoke test runner.
- `src/PhotoSelector.SmokeTest/PhotoSelector.SmokeTest.csproj`: Smoke test project.

Generated/Build outputs
- `src/**/bin/`: Build artifacts.
- `src/**/obj/`: Build intermediates.
- `src/.vs/`: Visual Studio workspace state.
