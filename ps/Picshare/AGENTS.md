# Picshare: guide for AI agents

## Keep this file current

Agents must keep this `AGENTS.md` accurate and up to date. When a task changes the project structure, architecture, workflows, configuration, or build/test commands described here, update the relevant sections as part of the same task. Correct outdated guidance when you discover it. Keep this file a concise description of the current project, not a task log.

## Project purpose

Picshare is a cross-platform photo album sharing and collaborative selection application. An author creates an album from local photos, publishes it to Google Drive or a local directory, and shares it with reviewers. Reviewers categorize photos, and the author collects their feedback over multiple rounds to finalize a selection toward a target number of Nice photos.

Core capabilities include:

- Importing photos from local folders, generating thumbnails, and publishing albums.
- Opening albums from Google Drive links/IDs, local manifest files, and `picshare://album` links.
- Reviewing photos in Uncategorized, Nice, Okay, and Trash categories, grouping duplicates, choosing a best duplicate, rotating photos, and bookmarking progress.
- Saving reviewer decisions locally, synchronizing feedback, tracking reviewer status, collecting feedback, starting new rounds, and finalizing results.
- Downloading photos by selection/category, resuming interrupted album creation, and displaying progress for long operations.
- Caching thumbnails and originals in memory/on disk and preloading images around the current view.
- Exporting a selected period of diagnostic logs as a ZIP from Settings > Advanced, with native mobile sharing and desktop file saving.

## Technology and repository layout

The application uses C# and .NET 10, Avalonia 11.3 (the shared version is in `Directory.Build.props`), CommunityToolkit.Mvvm, and SkiaSharp. Nullable reference types and compiled Avalonia bindings are enabled.

| Path | Responsibility |
| --- | --- |
| `Picshare/` | Shared application, UI, models, view models, and services targeting `net10.0`. |
| `Picshare/Models/` | Album manifests, photo references, reviewer identities, feedback documents, and workflow state. |
| `Picshare/ViewModels/` | Observable UI state and commands. `MainViewModel.cs` coordinates most application workflows. |
| `Picshare/Views/` | Avalonia `.axaml` views and code-behind, including album creation, review, photo viewer, settings, sidebar, and dialogs. |
| `Picshare/Services/` | Publishing, loading, feedback synchronization, Google API clients, caching, image loading, persistence, and retries. |
| `Picshare.Desktop/` | Desktop entry point and host, targeting `net10.0`. |
| `Picshare.Android/` | Android entry point and foreground service support, targeting `net10.0-android`. |
| `Picshare.iOS/` | iOS entry point and background task support, targeting `net10.0-ios`. |
| `Picshare.Tests/` | xUnit tests for shared application behavior. |
| `publish-android.ps1` | Release publish commands for Android x64 and arm64. |

`Picshare/App.axaml.cs` creates the shared `MainViewModel` and chooses a desktop window or single-view application host based on the Avalonia application lifetime.

## Architecture and persistence

- An album consists of an `album.json` manifest, photos/thumbnails, and a feedback folder. `AlbumManifest` describes the author, photo references, target selection count, and storage backend details.
- Storage uses local filesystem or Google Drive files/folders; there is no separate server application in this repository. Feedback databases are JSON documents, not a SQL database.
- `AlbumPublishingWorkflow` contains shared photo publishing/manifest logic. `LocalFileSystemAlbumPublisher`, `GoogleDriveAlbumPublisher`, and `PersistentAlbumCreationService` handle destination-specific and resumable creation work.
- `PhotoThumbnailGenerator` leaves caller-owned photo streams open when reading dimensions or creating thumbnails. Keep SkiaSharp stream wrappers configured with `disposeManagedStream: false`; resumable creation rewinds the same source stream between these operations.
- `ReviewerFeedbackService` handles reviewer decisions, synchronization, statuses, shared feedback, and review rounds. `IReviewerFeedbackBackend` has local filesystem and Google Drive implementations.
- `AlbumLoader` and `AlbumLinkParser` load manifests and interpret album references. Album card captions bind the cached `AlbumPhotoViewModel.ListDisplayName` numeric filename suffix (preserving leading zeros), falling back to the original name when there is no numeric suffix. All other filename displays keep `FileName`. The photo viewer Properties menu opens `PhotoPropertiesDialogView` with the full filename, human-readable and exact file size, pixel dimensions, and OK. `PhotoPropertiesService` reads local file length and image headers off the UI thread or requests authenticated Drive metadata without downloading the photo; legacy Drive URLs use the same strict URL recognition as image downloads. Other HTTP sources use response headers and manifest dimensions. Properties requests are canceled on dismissal, viewer closure, or photo changes, and stale completions cannot update another dialog. No manifest format changes are required.
- `ImageCacheService` manages image caches; `AlbumImageListLoader` coordinates visible image work and background warmup. Cancellation and generation tracking prevent stale work from updating a new view. Image worker UI assignments use Background dispatcher priority so input/rendering can run first. Album scrolling coalesces viewport/sticky-header bookkeeping at the same priority, tracks loaded card controls, and reads only realized row/header containers instead of repeatedly scanning the entire visual tree. Visual-tree access stays on the UI thread; slow bookkeeping passes (32 ms or more) are logged at most once per five seconds. Experimental scroll deferral is enabled by default (`Preload images while scrolling` unchecked in Advanced). `MainView.ImageScrolling.cs` uses `AlbumScrollLoadingPolicy` to pause list work on motion, resume below 120 DIP/s over a 100 ms sample or after 180 ms idle, and defer again above 240 DIP/s or on a large step. The loader pauses visible list and background warmup scheduling, suppresses late bitmap assignments, disposes unbound leases, and wakes workers for the current viewport on resume. Existing card bitmaps remain visible and explicit photo-viewer priorities stay active. View/tab changes and detachment reset the pause and idle timer.
- Drive manifests/photos/thumbnails download through authenticated `files.get?alt=media`, using a shared HTTP pipeline with `GoogleDriveMediaHandler`. It upgrades legacy `drive.google.com/uc` URLs from existing albums and attaches credentials only to recognized HTTPS Drive file requests. Opening a Drive link while signed out prompts sign-in before loading its manifest. Parallel image requests share a token-refresh gate and reuse valid tokens without rewriting the token store for each image. HTML download responses are rejected before caching; failed background cache decodes are skipped for the current loader generation instead of immediately requeued.
- Google integration uses REST clients for Drive, People, and user information, plus OAuth sign-in/token persistence. Album sharing searches previously used email addresses through `otherContacts.search`, which reads Google's automatically saved interaction contacts. OAuth requests `contacts.other.readonly`; existing sessions lacking it are prompted to sign out and sign in again. The first search in a session sends an empty-query warmup and waits five seconds for Google's lazy cache. Search diagnostics record counts without query text or contact details. There is no saved-contact-list fallback; results depend on what Google has saved in Other Contacts.
- `LongRunningOperationHost` abstracts platform support for lengthy work; mobile hosts provide Android foreground service and iOS background task integration.
- `DiagnosticLogging` uses a Trace minimum level (Avalonia `Verbose`, exported as `Trace`), capturing console/trace output, Avalonia events at all levels, workflow outcomes/exceptions, and HTTP request status/timing (without bodies, headers, or query strings). `DiagnosticLogService` queues events without waiting for storage; one background consumer performs message formatting, redaction, JSON serialization, writes, rotation and compression. It preserves the originating event timestamp/thread ID. The backlog is limited to 8,192 records and 4 Mi characters of message text (roughly 8 MiB UTF-16); overflow drops events and records a warning instead of blocking input or growing indefinitely. UI-owned Avalonia property values are converted to strings before queueing. A buffered writer holds the active JSONL file open under local application data in `Picshare/diagnostics`, flushing once per second and when the worker processes warnings/errors. Explicit flush/export and disposal drain prior accepted events; dispose independently owned instances to close file handles. Fatal runtime exceptions explicitly flush before exit. Chunks rotate at UTC day boundaries or 2 MiB and closed chunks are compressed to `.jsonl.gz`, preserving their original timestamps. Maintenance runs at startup, hourly while running, on rotation, and before export; it removes logs older than one calendar month (discarding the whole boundary day's chunk) and enforces a 64 MiB size limit. ZIP exports read active and compressed chunks and contain `logs.jsonl` filtered by event timestamp and `diagnostics.json` describing the requested interval and available coverage.
- Settings diagnostics exports use a desktop save picker or the mobile `IDiagnosticShareHost` registered in `DiagnosticExportHost`. Android uses a narrowly scoped cache `FileProvider`, temporary read permissions, and the system Sharesheet, including a Save to device activity backed by `ACTION_CREATE_DOCUMENT`. iOS uses `UIActivityViewController` with an iPad popover anchor. Mobile archives remain available for receiving apps and are cleaned up on later exports after one day.
- Photo viewer input differs by platform: desktop keeps its mouse-wheel zoom, pointer anchoring, dragging, and slider; Android/iOS use Avalonia's built-in pinch/scroll recognizers, gesture-start scale/origin snapshots, and touch panning. At fit size, both paths show `PhotoViewerSwipeControl` centered at the swipe's starting point: left/right invoke next/previous with matching action icons, up moves to Nice, and down moves to Good (the persisted `ok`/Okay category). The wheel buttons have explicit centered 40-DIP bounds. Swipes use a 6-DIP radial tap tolerance and choose the closest axis, with exact diagonals choosing horizontally; mobile uses the full contact displacement and commits short swipes even before framework scrolling begins. `PhotoViewerView.Swipe.cs` shares selection and completion handling; release hides the circle before invoking an available action, while pinch, capture loss, image changes, and closing cancel it. Mobile's scroll recognizer subclass distinguishes release from capture-loss completion. A mobile-only pinch recognizer subclass preserves normal single-contact tap recognition on release (Avalonia's base recognizer otherwise suppresses it), while multi-contact gestures and pans still suppress taps. Short taps on the image or its background toggle the same controls-visibility command as desktop clicks. Mobile hides the mouse-oriented zoom slider and clamps rotated image bounds separately. Keep mobile changes behind platform checks so desktop interaction stays unchanged. The footer's icon-only rotation shortcuts share the square-button sizing policy; its Move to and Go to submenus reuse the same commands and availability rules as the buttons.
- Photo/album downloads on Android/iOS use the native save picker and `IStorageFile.OpenWriteAsync`, never `TryGetLocalPath` for a selected document (`content://` is normal on Android). A single photo saves its original bytes; selected photos and chosen album categories save as one ZIP, with category subfolders for albums and Include/Exclude choices. `MobilePhotoDownloadService` stages data in a private temporary directory before opening the possibly non-seekable document output stream, allowing safe network retries and cancellation. Desktop retains its existing file/folder destinations and per-category archive modes.
- Settings rows resize in code-behind using shared label/input sizing. Diagnostics uses the cache input's preferred width for the units selector and send button (capped to fit the label column), aligns "for the last" with the cache input headers, and assigns extra space to the numeric input. Once numeric reaches the units width, the first four columns shrink proportionally while the final ellipsis keeps its width at the right edge. Numeric spinner buttons hide below the same width threshold as other settings inputs.

## Configuration

`PicshareSettingsProvider` loads embedded `picshare.settings.json`, then merges available overrides from the application directory and `Picshare/settings.json` under the application-data and local-application-data directories. Settings include Google OAuth configuration, an optional local storage root, and UI defaults. See `picshare.settings.example.json` for the current schema. Each platform host packages its own settings file. Settings > View independently controls the photo viewer navigation, category, and rotation footer buttons. `defaultSettings.categoryButtons` defaults to true on desktop and false on Android/iOS; `defaultSettings.rotationButtons` is true on all platforms. Nullable local preferences override these defaults while older user settings inherit them. Hiding shortcuts leaves menu and swipe actions available. `defaultSettings.preloadImagesWhileScrolling` is false in every packaged settings file; checking the persisted Advanced option restores loading during scrolling.

`LocalUserSettingsStore`, `GoogleOAuthTokenStore`, and `AlbumOpenHistoryStore` persist user preferences, OAuth tokens, and recent album history respectively. Do not copy credentials or tokens into documentation, tests, or logs.

## Build, run, and validation

Run commands from the repository root with a .NET 10 SDK:

```powershell
dotnet build Picshare.Desktop/Picshare.Desktop.csproj
dotnet run --project Picshare.Desktop/Picshare.Desktop.csproj
dotnet test Picshare.Tests/Picshare.Tests.csproj
git diff --check
```

Building the entire `Picshare.sln` includes mobile targets and requires their workloads/platform toolchains; use the desktop project for desktop-only work. Android and iOS changes should also be built against the affected host when its tooling is available. The Android publish script runs Release publishes for both supported architectures.

Existing tests cover image cache warmup and failed-work suppression, image loading lifecycle/concurrency, authenticated Drive media requests and credential restrictions, HTML response rejection, thumbnail source-stream ownership and resumable album creation, mobile non-seekable downloads/ZIPs/retry/cancellation, mobile tap events and suppression during pinch/pan alongside pinch anchoring/scale/rotated bounds, list caption extraction, local/Drive photo properties and credential isolation, properties dialog dismissal/cancellation, photo viewer swipe direction/placement/availability/cancellation, mobile release versus capture loss, button settings merging/serialization/visibility, scroll speed/idle policy and loader pause/resume/queued bitmap suppression, and reversed wheel navigation bindings, desktop category download options, transient retry behavior, OAuth refresh failures and requested scopes, Other Contacts search/warmup/response parsing, loading local reviewer feedback, diagnostics buffering/filtering/redaction/concurrency/compressed rotation/calendar-month retention, responsive diagnostics column sizing, and Trace-level event acceptance. Run relevant tests for behavioral changes and report any checks that could not run. Documentation-only edits do not require application builds or tests.

## Working conventions

- Inspect `git status` before editing and preserve existing user changes, including untracked files. Keep changes focused on the requested task.
- Follow the existing MVVM patterns, observable properties, relay commands, compiled bindings, and view/code-behind organization.
- Keep platform-independent behavior in the shared project and native lifecycle/service integration in the platform hosts.
- Preserve compatibility with existing album manifests and feedback documents when changing persisted models or serialization.
- For image loading or asynchronous workflow changes, preserve cancellation, concurrency limits, UI-thread requirements, and protection against stale completions.
- Prefer focused service/view changes over adding unrelated responsibilities to the already large `MainViewModel`.
- Keep generated build output, caches, and runtime album data out of source changes.
