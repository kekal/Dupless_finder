# Dupless Finder: WPF to Client-Side Web App Conversion Plan

## Architecture Overview

```
WPF Desktop                          Web (Blazor WASM Static)
─────────────────                     ──────────────────────────
Prism + DryIoc          →             Blazor WASM + built-in DI
MainWindow.xaml          →             Razor components
MainViewModel            →             Component code-behind / services
OpenCvSharp4             →             opencv.js via JS interop
SQLite + EF Core         →             IndexedDB via idb (JS library)
File.Move / FileInfo     →             File System Access API (Chrome)
Task.Run / Parallel.For  →             Web Workers (via raw JS)
VirtualizingWrapPanel    →             Blazor <Virtualize> + CSS Grid
System.Reactive          →             System.Reactive (works in WASM)
```

## Hosting: Azure Static Web Apps (Free Tier)

- **No backend server.** Blazor WASM compiles to a set of static files (.html, .js, .wasm, .dll).
- Azure Static Web Apps free tier: custom domain, SSL, global CDN — zero cost for a client-only site.
- Deploy via `dotnet publish` → upload `/bin/Release/net8.0/publish/wwwroot/` to Azure.
- No App Service, no Functions needed.

---

## Phase 1: Project Scaffold

### 1.1 Create Blazor WebAssembly Standalone project

```
dotnet new blazorwasm -o DuplessFinder.Web --no-https
```

Target `net8.0`. No ASP.NET hosted backend — this produces only static files.

### 1.2 Project structure mapping

```
DuplessFinder.Web/
├── wwwroot/
│   ├── index.html
│   ├── js/
│   │   ├── opencv-interop.js      ← opencv.js wrapper
│   │   ├── indexeddb-interop.js    ← IndexedDB wrapper
│   │   └── file-access-interop.js  ← File System Access API wrapper
│   └── lib/
│       └── opencv.js               ← ~8MB, loaded on demand
├── Pages/
│   └── Index.razor                 ← Main page (replaces MainWindow.xaml)
├── Components/
│   ├── ThumbnailGrid.razor         ← Virtualized image gallery
│   ├── PairGrid.razor              ← Duplicate pairs display
│   ├── ImagePreview.razor          ← Full-size preview with zoom
│   └── Toolbar.razor               ← Open, Analyze, slider, progress
├── Services/
│   ├── IFileAccessService.cs       ← JS interop: File System Access API
│   ├── FileAccessService.cs
│   ├── IOpenCvService.cs           ← JS interop: opencv.js SIFT
│   ├── OpenCvService.cs
│   ├── ICacheService.cs            ← JS interop: IndexedDB
│   ├── CacheService.cs
│   ├── ICalcOperations.cs          ← Ported from desktop (orchestration only)
│   ├── CalcOperations.cs
│   ├── ILoadingOperations.cs
│   └── LoadingOperations.cs
├── Models/
│   ├── ImageInfo.cs                ← Simplified, no WPF dependencies
│   ├── PairSimilarityInfo.cs
│   └── ImagePair.cs
├── Workers/
│   └── sift-worker.js              ← Web Worker for SIFT computation
└── Program.cs                      ← DI registration
```

### 1.3 DI registration (Program.cs)

Current Prism DryIoc registrations map directly to Blazor's built-in `IServiceCollection`:

```csharp
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddSingleton<IFileAccessService, FileAccessService>();
builder.Services.AddSingleton<IOpenCvService, OpenCvService>();
builder.Services.AddTransient<ICalcOperations, CalcOperations>();
builder.Services.AddTransient<ILoadingOperations, LoadingOperations>();
```

---

## Phase 2: File System Access (replaces `DirectoryInfo` + `File.Move`)

### 2.1 The API

The [File System Access API](https://developer.mozilla.org/en-US/docs/Web/API/File_System_Access_API) is Chrome 86+ (Edge, Opera too). It gives you:

- `window.showDirectoryPicker()` → returns a `FileSystemDirectoryHandle`
- Recursive enumeration of files within the directory
- Read file contents as `ArrayBuffer` / `Blob`
- **Write and delete** files (with user permission grant)

This replaces `LoadingOperations.ScanImageFileInfos()` and the delete/undo in `MainViewModel`.

### 2.2 JS interop layer (`file-access-interop.js`)

```javascript
// Store the directory handle globally so it persists across calls
let directoryHandle = null;

export async function pickDirectory() {
    directoryHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
    return directoryHandle.name;
}

export async function scanImages(includeSubfolders) {
    const results = [];
    await scanDir(directoryHandle, '', includeSubfolders, results);
    return results; // [{path, name, size, lastModified}, ...]
}

async function scanDir(dirHandle, prefix, recursive, results) {
    for await (const [name, handle] of dirHandle.entries()) {
        const path = prefix ? `${prefix}/${name}` : name;
        if (handle.kind === 'file') {
            const ext = name.split('.').pop().toLowerCase();
            if (['jpg','jpeg','png','bmp','tiff','tif','webp'].includes(ext)) {
                const file = await handle.getFile();
                results.push({
                    path, name, size: file.size,
                    lastModified: file.lastModified
                });
            }
        } else if (handle.kind === 'directory' && recursive) {
            await scanDir(handle, path, true, results);
        }
    }
}

export async function readFileBytes(relativePath) {
    const handle = await resolveFile(directoryHandle, relativePath);
    const file = await handle.getFile();
    return new Uint8Array(await file.arrayBuffer());
}

export async function deleteFile(relativePath) {
    const parts = relativePath.split('/');
    const fileName = parts.pop();
    let dir = directoryHandle;
    for (const p of parts) dir = await dir.getDirectoryHandle(p);
    await dir.removeEntry(fileName);
}

export async function moveToDeleted(relativePath) {
    // Create .deleted subfolder, copy file there, remove original
    const deletedDir = await directoryHandle
        .getDirectoryHandle('.deleted', { create: true });
    const parts = relativePath.split('/');
    const fileName = parts.pop();
    // ... read original, write to .deleted, remove original
}
```

### 2.3 C# interop service

```csharp
public class FileAccessService : IFileAccessService, IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _module;

    public FileAccessService(IJSRuntime js)
    {
        _module = new(() => js.InvokeAsync<IJSObjectReference>(
            "import", "./js/file-access-interop.js").AsTask());
    }

    public async Task<string> PickDirectoryAsync()
    {
        var mod = await _module.Value;
        return await mod.InvokeAsync<string>("pickDirectory");
    }

    public async Task<List<FileEntry>> ScanImagesAsync(bool includeSubfolders)
    {
        var mod = await _module.Value;
        return await mod.InvokeAsync<List<FileEntry>>("scanImages", includeSubfolders);
    }

    public async Task<byte[]> ReadFileBytesAsync(string path)
    {
        var mod = await _module.Value;
        return await mod.InvokeAsync<byte[]>("readFileBytes", path);
    }
}
```

### 2.4 Browser compatibility note

File System Access API is **Chromium-only** (Chrome, Edge, Opera). Firefox and Safari do not support it. Since this targets "modern Chrome", that is fine. Add a startup check:

```javascript
if (!('showDirectoryPicker' in window)) {
    // Show "Please use Chrome or Edge" message
}
```

---

## Phase 3: IndexedDB (replaces SQLite + EF Core)

### 3.1 What to store (mirrors current schema)

SQLite stores three things — map them to IndexedDB object stores:

| SQLite Table | IndexedDB Store | Key | Indexes |
|---|---|---|---|
| `Photo` (thumbnails) | `thumbnails` | `fingerprint` (= `{size}_{lastModified}`) | — |
| `Photo` (SIFT descriptors) | `sift_descriptors` | `fingerprint` | — |
| `SimilarityResult` | `similarity_results` | `[photo1Id, photo2Id]` (compound) | `score` |

### 3.2 JS interop (`indexeddb-interop.js`)

Use the raw `idb` library (tiny, ~1KB) or plain IndexedDB API:

```javascript
let db = null;

export async function openDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open('dupless_cache', 1);
        request.onupgradeneeded = (e) => {
            const db = e.target.result;
            db.createObjectStore('thumbnails', { keyPath: 'fingerprint' });
            db.createObjectStore('sift_descriptors', { keyPath: 'fingerprint' });
            const simStore = db.createObjectStore('similarity_results',
                { keyPath: ['photo1Fingerprint', 'photo2Fingerprint'] });
            simStore.createIndex('score', 'score');
        };
        request.onsuccess = (e) => { db = e.target.result; resolve(); };
        request.onerror = (e) => reject(e);
    });
}

export async function getThumbnail(fingerprint) { /* tx.get */ }
export async function putThumbnailBatch(items) { /* single tx, multiple puts */ }
export async function getSiftDescriptor(fingerprint) { /* tx.get */ }
export async function putSiftDescriptor(fingerprint, data, rows, cols) { /* tx.put */ }
export async function getAllThumbnails() { /* tx.getAll — for preload */ }
```

### 3.3 Mapping current patterns

`PhotoDbService` patterns port directly:

| Desktop (EF Core) | Web (IndexedDB) |
|---|---|
| `PreloadThumbnailsAsync()` → `SELECT * FROM Photo` | `store.getAll()` on `thumbnails` |
| `TryGetCachedSift(fingerprint)` | `store.get(fingerprint)` on `sift_descriptors` |
| Batch thumbnail writes (queue of 500) | Single IndexedDB transaction with N `put()` calls |
| `SemaphoreSlim(1,1)` gate | Not needed — IndexedDB is async but single-threaded |

### 3.4 Storage limits

IndexedDB has no hard limit in Chrome — it can use up to 80% of disk space. For a cache of thumbnails and SIFT descriptors, you'll never hit a limit. The browser may prompt the user if storage exceeds ~50MB without persistence granted.

Call `navigator.storage.persist()` on first use to prevent the browser from evicting your cache.

---

## Phase 4: OpenCV.js (replaces OpenCvSharp4)

This is the most complex port. The app uses three OpenCV features: image decode/resize, SIFT, and FLANN matching.

### 4.1 Getting opencv.js with SIFT

SIFT is in `opencv_contrib`, not the default build. You need a custom build or the "contrib" build from the official OpenCV.js releases.

**Build option** (recommended for size control):

```bash
# Build opencv.js with only the modules you need
python platforms/js/build_js.py build_js \
    --build_wasm \
    --cmake_option="-DOPENCV_EXTRA_MODULES_PATH=../../opencv_contrib/modules" \
    --cmake_option="-DBUILD_LIST=core,imgproc,features2d,flann,xfeatures2d"
```

This produces a WASM build (~4-8MB) with exactly: Mat, resize, SIFT, FLANN.

**Alternative**: Use the prebuilt opencv.js 4.x contrib WASM from GitHub releases (~8MB).

### 4.2 Loading strategy

opencv.js is large. Load it lazily — only when the user clicks "Analyze":

```javascript
let cv = null;

export async function ensureLoaded() {
    if (cv) return;
    return new Promise((resolve) => {
        const script = document.createElement('script');
        script.src = './lib/opencv.js';
        script.onload = () => {
            cv = window.cv;
            cv.onRuntimeInitialized = resolve;
        };
        document.head.appendChild(script);
    });
}
```

In the Web Worker variant (Phase 6), opencv.js is loaded inside the worker instead.

### 4.3 JS interop (`opencv-interop.js`)

Maps current `CalcOperations` and `MatSerializer`:

```javascript
export function decodeThumbnail(imageBytes, maxSize) {
    // Decode image from byte array
    const mat = cv.matFromImageData(/* ... */);
    // Or: cv.imdecode(rawDataMat, cv.IMREAD_COLOR)
    const resized = new cv.Mat();
    const scale = maxSize / Math.max(mat.rows, mat.cols);
    cv.resize(mat, resized, new cv.Size(0, 0), scale, scale);

    // Encode to JPEG for thumbnail storage
    const canvas = document.createElement('canvas');
    cv.imshow(canvas, resized);
    const jpegBlob = canvas.toDataURL('image/jpeg', 0.8);

    mat.delete(); resized.delete();
    return jpegBlob;
}

export function computeSift(imageBytes) {
    // Decode to Mat (200px max dimension, matching StoredMatDecodeSize)
    const mat = decodeAndResize(imageBytes, 200);
    const gray = new cv.Mat();
    cv.cvtColor(mat, gray, cv.COLOR_RGBA2GRAY);

    const sift = new cv.SIFT(0, 3, 0.04, 10, 1.6);
    const keypoints = new cv.KeyPointVector();
    const descriptors = new cv.Mat();
    sift.detectAndCompute(gray, new cv.Mat(), keypoints, descriptors);

    // Serialize descriptors to transferable ArrayBuffer
    const rows = descriptors.rows;
    const cols = descriptors.cols;
    const data = new Float32Array(descriptors.data32F); // copy

    keypoints.delete(); gray.delete(); mat.delete();
    sift.delete(); descriptors.delete();

    return { rows, cols, data: data.buffer };
}

export function matchPair(desc1, desc2) {
    // Reconstruct Mats from serialized data
    const mat1 = matFromFloat32(desc1.data, desc1.rows, desc1.cols);
    const mat2 = matFromFloat32(desc2.data, desc2.rows, desc2.cols);

    const matcher = new cv.BFMatcher(cv.NORM_L2); // or FlannBasedMatcher
    const matches = new cv.DMatchVectorVector();
    matcher.knnMatch(mat1, mat2, matches, 2);

    // Lowe's ratio test (0.7) — identical to the C# code
    let goodCount = 0;
    for (let i = 0; i < matches.size(); i++) {
        const m = matches.get(i);
        if (m.size() >= 2 && m.get(0).distance < 0.7 * m.get(1).distance) {
            goodCount++;
        }
    }

    mat1.delete(); mat2.delete(); matcher.delete(); matches.delete();
    return goodCount > 0 ? 1000.0 / goodCount : Number.MAX_VALUE;
}
```

### 4.4 FlannBasedMatcher caveat

opencv.js's FLANN bindings can be incomplete in some builds. If FLANN is unavailable, fall back to `BFMatcher(cv.NORM_L2)` — it's slower for large descriptor sets but functionally identical for this use case (matching pairs, not searching a database). Images are resized to 200px, so descriptor counts are small (~50-200 per image). BFMatcher will be fast enough.

### 4.5 Thumbnail generation — simpler alternative

For thumbnails specifically, OpenCV is not needed. Use the Canvas API:

```javascript
export async function generateThumbnail(file, maxSize) {
    const img = await createImageBitmap(file);
    const scale = maxSize / Math.max(img.width, img.height);
    const canvas = new OffscreenCanvas(
        Math.round(img.width * scale),
        Math.round(img.height * scale)
    );
    const ctx = canvas.getContext('2d');
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);
    const blob = await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.8 });
    return new Uint8Array(await blob.arrayBuffer());
}
```

This replaces the 5-stage thumbnail pipeline. In a browser there is no need for EXIF extraction or Shell API — `createImageBitmap()` handles all supported formats natively and is hardware-accelerated.

---

## Phase 5: UI (replaces WPF XAML)

### 5.1 Main layout (`Index.razor`)

Current `MainWindow.xaml` is a 2-column layout: left (gallery or pairs), right (preview). In Blazor:

```razor
@page "/"
@inject IFileAccessService FileAccess
@inject ICalcOperations Calc
@inject ICacheService Cache

<div class="app-layout">
    <Toolbar OnOpen="OpenFolder" OnAnalyze="Analyze" OnCancel="Cancel"
             OnUndo="Undo" ScoreThreshold="@scoreThreshold"
             ScoreThresholdChanged="OnThresholdChanged"
             Progress="@progress" StatusText="@statusText"
             IncludeSubfolders="@includeSubfolders" />

    <div class="content-area">
        <div class="left-panel">
            @if (showPairs)
            {
                <PairGrid Pairs="@filteredPairs"
                          OnDeleteImage="DeleteImage"
                          OnPreviewImage="ShowPreview" />
            }
            else
            {
                <ThumbnailGrid Images="@images"
                               OnImageClick="ShowPreview"
                               OnDeleteImage="DeleteImage" />
            }
        </div>
        <div class="right-panel">
            @if (previewImage is not null)
            {
                <ImagePreview Image="@previewImage"
                              AlternateImage="@alternateImage"
                              OnClose="ClosePreview" />
            }
        </div>
    </div>
</div>
```

### 5.2 Virtualized thumbnail grid (`ThumbnailGrid.razor`)

Replaces `VirtualizingWrapPanel`. Blazor has a built-in `<Virtualize>` component, but it's list-only (single column). For a wrap grid, combine CSS Grid with `<Virtualize>`:

```razor
<div class="thumbnail-grid" style="height:100%; overflow-y:auto;">
    <Virtualize Items="@Images" Context="img" OverscanCount="20">
        <div class="thumbnail-cell" @onclick="() => OnImageClick.InvokeAsync(img)">
            <img src="@img.ThumbnailDataUrl" loading="lazy" />
            <span class="filename">@img.Name</span>
            <button class="delete-btn" @onclick:stopPropagation
                    @onclick="() => OnDeleteImage.InvokeAsync(img)">×</button>
        </div>
    </Virtualize>
</div>
```

```css
.thumbnail-grid {
    display: flex;
    flex-wrap: wrap;
    align-content: flex-start;
}
.thumbnail-cell {
    width: 200px; height: 230px;
    position: relative;
    margin: 4px;
}
.thumbnail-cell img {
    width: 200px; height: 200px;
    object-fit: cover;
}
```

**Note**: Blazor's `<Virtualize>` in a flex-wrap context works if the container has a fixed height and `ItemSize` is set to the row height. For truly large collections (10K+ images), a JS-based virtual scroller via interop may be needed (e.g., `tanstack/virtual`).

### 5.3 Pair grid (`PairGrid.razor`)

Replaces the WPF DataGrid. A simple virtualized list of pair rows:

```razor
<div class="pair-grid">
    <Virtualize Items="@Pairs" Context="pair" ItemSize="220">
        <div class="pair-row">
            <div class="pair-image"
                 @onclick="() => OnPreviewImage.InvokeAsync((pair.Image1, pair.Image2))">
                <img src="@pair.Image1.ThumbnailDataUrl" />
                <button class="delete-btn" @onclick:stopPropagation
                        @onclick="() => OnDeleteImage.InvokeAsync(pair.Image1)">×</button>
            </div>
            <div class="pair-score">@pair.Score.ToString("F1")</div>
            <div class="pair-image"
                 @onclick="() => OnPreviewImage.InvokeAsync((pair.Image2, pair.Image1))">
                <img src="@pair.Image2.ThumbnailDataUrl" />
                <button class="delete-btn" @onclick:stopPropagation
                        @onclick="() => OnDeleteImage.InvokeAsync(pair.Image2)">×</button>
            </div>
        </div>
    </Virtualize>
</div>
```

### 5.4 Image preview with zoom

Replaces the WPF `ScrollViewer` + `Image` with mouse-wheel zoom:

```razor
<div class="preview-container" @onwheel="OnWheel" @oncontextmenu="Close"
     @oncontextmenu:preventDefault>
    <img src="@fullImageUrl"
         style="transform: scale(@zoom) translate(@(panX)px, @(panY)px);"
         @onmousedown="StartDrag" @onmousemove="Drag" @onmouseup="EndDrag" />
</div>

@code {
    private double zoom = 1.0;
    private void OnWheel(WheelEventArgs e)
    {
        zoom *= e.DeltaY < 0 ? 1.2 : 0.8;
        zoom = Math.Clamp(zoom, 0.1, 10.0);
    }
}
```

For the full-resolution image, read the file bytes via File System Access API on demand and create an object URL:

```javascript
export async function createObjectUrl(relativePath) {
    const handle = await resolveFile(directoryHandle, relativePath);
    const file = await handle.getFile();
    return URL.createObjectURL(file);
}
```

---

## Phase 6: Web Workers (replaces `Task.Run` / `Parallel.For`)

This is critical for performance. Without workers, SIFT computation blocks the UI thread (Blazor WASM is single-threaded).

### 6.1 Architecture

```
Main Thread (Blazor WASM)          Web Workers (pure JS)
──────────────────────────          ─────────────────────
UI rendering                        Worker 1: SIFT hash computation
Orchestration                       Worker 2: SIFT hash computation
Progress display                    Worker 3: Pair matching
IndexedDB reads/writes              Worker 4: Pair matching
                                    (pool of N workers)

Communication: postMessage() with Transferable ArrayBuffers
```

### 6.2 Worker pool manager (`worker-pool.js`)

```javascript
class WorkerPool {
    constructor(scriptUrl, poolSize) {
        this.workers = Array.from({ length: poolSize },
            () => new Worker(scriptUrl));
        this.queue = [];
        this.free = [...this.workers];
    }

    async execute(taskData) {
        return new Promise((resolve) => {
            const run = (worker) => {
                worker.onmessage = (e) => {
                    this.free.push(worker);
                    this.drainQueue();
                    resolve(e.data);
                };
                // Transfer ArrayBuffers for zero-copy
                const transfers = taskData.transfers || [];
                worker.postMessage(taskData, transfers);
            };

            if (this.free.length > 0) {
                run(this.free.pop());
            } else {
                this.queue.push(run);
            }
        });
    }

    drainQueue() {
        if (this.queue.length > 0 && this.free.length > 0) {
            const next = this.queue.shift();
            next(this.free.pop());
        }
    }
}
```

### 6.3 SIFT worker (`sift-worker.js`)

```javascript
// Load opencv.js inside the worker (no DOM access, but WASM works)
importScripts('./lib/opencv.js');

// Wait for WASM initialization
let ready = new Promise(resolve => {
    cv.onRuntimeInitialized = resolve;
});

self.onmessage = async function(e) {
    await ready;
    const { type, payload } = e.data;

    if (type === 'computeSift') {
        const { imageBytes, fingerprint } = payload;
        const result = computeSift(imageBytes);
        // Transfer the Float32Array buffer (zero-copy)
        self.postMessage(
            { fingerprint, ...result },
            [result.data]
        );
    }
    else if (type === 'matchPair') {
        const { desc1, desc2, id } = payload;
        const score = matchPair(desc1, desc2);
        self.postMessage({ id, score });
    }
};
```

### 6.4 C# orchestration

From Blazor, dispatch work to the pool via JS interop:

```csharp
public async Task<Dictionary<string, SiftResult>> CalcSiftHashes(
    List<ImageInfo> images, IProgress<int> progress, CancellationToken ct)
{
    var results = new Dictionary<string, SiftResult>();
    var module = await _jsModule.Value;

    // Initialize worker pool (navigator.hardwareConcurrency workers)
    await module.InvokeVoidAsync("initWorkerPool");

    var tasks = images.Select(async img =>
    {
        ct.ThrowIfCancellationRequested();

        // Check IndexedDB cache first
        var cached = await _cache.GetSiftDescriptor(img.Fingerprint);
        if (cached is not null)
            return (img.Fingerprint, cached);

        // Read image bytes and send to worker
        var bytes = await _fileAccess.ReadFileBytesAsync(img.Path);
        var result = await module.InvokeAsync<SiftResult>(
            "computeSiftInWorker", bytes, img.Fingerprint);

        // Cache in IndexedDB
        await _cache.PutSiftDescriptor(img.Fingerprint, result);

        progress.Report(Interlocked.Increment(ref count));
        return (img.Fingerprint, result);
    });

    // In WASM, actual parallelism is in the workers, not C# threads
    foreach (var task in tasks)
    {
        var (fp, result) = await task;
        results[fp] = result;
    }

    return results;
}
```

### 6.5 Matching parallelism

The current `Parallel.For` with `MaxDegreeOfParallelism=8` for pair matching maps to dispatching match jobs across the worker pool. Each worker runs one `matchPair` at a time. With 4-8 workers, you get true parallel SIFT matching.

### 6.6 SharedArrayBuffer consideration

For maximum performance, `SharedArrayBuffer` can share SIFT descriptors across workers without copying. This requires COOP/COEP headers:

```
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Embedder-Policy: require-corp
```

Azure Static Web Apps supports custom headers via `staticwebapp.config.json`. This is optional — `Transferable` ArrayBuffers are sufficient for most workloads.

---

## Phase 7: Porting the Analysis Pipeline

### 7.1 Loading pipeline (maps to `MainViewModel.LoadImages()`)

```
User clicks "Open Folder"
    → showDirectoryPicker() — browser grants access
    → scanImages(includeSubfolders) — enumerate files
    → Batch into groups of 100 (like the Observable.Buffer)
    → For each batch: add to images list, trigger Blazor re-render
    → For each image: generate thumbnail
        → Check IndexedDB cache by fingerprint
        → If miss: read file bytes, Canvas API resize, store in IndexedDB
    → Display progress (X / total)
```

### 7.2 Analysis pipeline (maps to `MainViewModel.CalcAndCompare()`)

```
User clicks "Analyze"
    → Load opencv.js (lazy, one-time ~3s)
    → Initialize Web Worker pool
    → Phase 1: SIFT hashing
        → For each image:
            → Check IndexedDB for cached descriptors
            → If miss: read file bytes, dispatch to worker
            → Worker: decode → resize(200px) → SIFT.detectAndCompute()
            → Worker returns: { rows, cols, Float32Array }
            → Store in IndexedDB
        → Progress: X/N images hashed
    → Phase 2: Pair matching
        → Generate all unique pairs: n*(n-1)/2
        → Dispatch pairs to worker pool
        → Worker: FlannMatcher.knnMatch → Lowe's ratio test → score
        → Collect results
        → Progress: X/M pairs compared
    → Store similarity results in IndexedDB
    → Filter by threshold, populate pairs list
    → Show pair grid
```

### 7.3 Score threshold filtering

The Rx-based debounce on slider change works in Blazor WASM — `System.Reactive` runs fine in WASM. Or use a simpler timer-based debounce:

```csharp
private Timer? _debounceTimer;

private void OnThresholdChanged(double value)
{
    scoreThreshold = value;
    _debounceTimer?.Dispose();
    _debounceTimer = new Timer(_ =>
    {
        InvokeAsync(() =>
        {
            filteredPairs = allPairs.Where(p => p.Score <= scoreThreshold).ToList();
            StateHasChanged();
        });
    }, null, 200, Timeout.Infinite);
}
```

---

## Phase 8: Delete / Undo (replaces `File.Move`)

### 8.1 Delete

Current approach: move file to `.deleted/` subfolder. This maps directly:

```javascript
export async function moveToDeleted(relativePath) {
    const parts = relativePath.split('/');
    const fileName = parts.pop();

    // Navigate to parent directory
    let parentDir = directoryHandle;
    for (const p of parts) parentDir = await parentDir.getDirectoryHandle(p);

    // Create .deleted in root
    const deletedDir = await directoryHandle
        .getDirectoryHandle('.deleted', { create: true });

    // Read original file
    const fileHandle = await parentDir.getFileHandle(fileName);
    const file = await fileHandle.getFile();
    const data = await file.arrayBuffer();

    // Write to .deleted/
    let destName = fileName;
    let counter = 0;
    while (true) {
        try {
            await deletedDir.getFileHandle(destName);
            counter++;
            destName = `${fileName.replace(/(\.[^.]+)$/, `_${counter}$1`)}`;
        } catch {
            break; // File doesn't exist, name is available
        }
    }
    const newHandle = await deletedDir.getFileHandle(destName, { create: true });
    const writable = await newHandle.createWritable();
    await writable.write(data);
    await writable.close();

    // Remove original
    await parentDir.removeEntry(fileName);

    return destName; // For undo stack
}
```

### 8.2 Undo stack

Identical to the current `Stack<(string, string)>` — just a C# stack in the Blazor component:

```csharp
private Stack<(string originalPath, string deletedName)> undoStack = new();

private async Task DeleteImage(ImageInfo img)
{
    var deletedName = await FileAccess.MoveToDeletedAsync(img.Path);
    undoStack.Push((img.Path, deletedName));
    images.Remove(img);
    filteredPairs.RemoveAll(p => p.Image1 == img || p.Image2 == img);
}

private async Task Undo()
{
    if (undoStack.Count == 0) return;
    var (original, deleted) = undoStack.Pop();
    await FileAccess.RestoreFromDeletedAsync(original, deleted);
    // Re-add to images, reload thumbnail, re-filter pairs
}
```

---

## Phase 9: Deployment

### 9.1 Azure Static Web Apps setup

```json
// staticwebapp.config.json
{
    "navigationFallback": {
        "rewrite": "/index.html"
    },
    "globalHeaders": {
        "Cross-Origin-Opener-Policy": "same-origin",
        "Cross-Origin-Embedder-Policy": "require-corp"
    },
    "mimeTypes": {
        ".wasm": "application/wasm",
        ".dll": "application/octet-stream"
    }
}
```

### 9.2 Build and deploy

```bash
dotnet publish -c Release
# Output: bin/Release/net8.0/publish/wwwroot/
# Contains: index.html, _framework/*.dll, _framework/*.wasm, js/*, lib/opencv.js
```

Deploy via:
- **GitHub Actions** (free, automatic on push) — Azure Static Web Apps has a built-in GitHub integration
- Or `az staticwebapp deploy` CLI

### 9.3 Size budget

| Asset | Size (approx) |
|---|---|
| Blazor WASM runtime + framework DLLs | ~8-12 MB (compressed ~3 MB with Brotli) |
| opencv.js (custom SIFT build) | ~4-8 MB (compressed ~2 MB) |
| Application DLLs | ~200 KB |
| JS interop files | ~10 KB |
| **Total first load** | **~5-6 MB compressed** |

Enable Brotli compression in the publish config:

```xml
<PropertyGroup>
    <BlazorEnableCompression>true</BlazorEnableCompression>
</PropertyGroup>
```

opencv.js loads lazily (only on "Analyze"), so initial page load is ~3MB.

---

## Phase 10: Component Mapping Summary

| Desktop Component | Web Replacement | Effort |
|---|---|---|
| `MainViewModel` (400+ lines) | `Index.razor` code-behind + services | Medium — logic stays, remove WPF-isms |
| `MainWindow.xaml` | `Index.razor` + child components | Medium — rewrite in HTML/CSS |
| `ImageInfo` (thumbnail pipeline) | Simplified model + Canvas API thumbnails | Low — much simpler in browser |
| `CalcOperations` (SIFT + matching) | JS Workers + opencv.js interop | High — core algorithm port |
| `PhotoDbService` (EF Core + SQLite) | `CacheService` (IndexedDB interop) | Medium — simpler API surface |
| `ThumbnailService` (5-stage pipeline) | Canvas `createImageBitmap` + resize | Low — browser does the heavy lifting |
| `LoadingOperations` (file scan) | File System Access API interop | Low — fewer lines |
| `MatSerializer` | Float32Array serialization in JS | Low — native in JS |
| `PerfLogger` | `console.time()` / `performance.mark()` | Low |
| `RangeObservableCollection` | Standard `List<T>` + `StateHasChanged()` | Not needed — Blazor diffs the DOM |
| `VirtualizingWrapPanel` | `<Virtualize>` + CSS Grid | Low-Medium |
| Delete/Undo system | File System Access API + C# stack | Low — same logic |

---

## Risks and Mitigations

| Risk | Mitigation |
|---|---|
| opencv.js SIFT not in default build | Custom build with contrib modules, or use prebuilt contrib WASM |
| FLANN unavailable in opencv.js | Fall back to BFMatcher (slower but correct). Images are 200px so descriptor count is small. |
| File System Access API is Chrome-only | Accept Chrome/Edge requirement. Show clear browser compatibility message. |
| WASM single-threaded blocks UI during interop marshaling | Keep data transfers minimal. Use Transferable ArrayBuffers. Do heavy work in workers. |
| Large collections (10K+ images) strain browser memory | Stream thumbnails, don't hold all Mats in memory. Workers free Mats after hashing. Use IndexedDB as primary store, not RAM. |
| opencv.js WASM memory limit (~2-4 GB) | Process images one at a time in workers. Each worker has its own memory space. |

---

## Recommended Implementation Order

1. **Scaffold** — Blazor WASM project, basic layout, toolbar
2. **File Access** — Pick folder, scan images, display file list
3. **Thumbnails** — Canvas API generation, IndexedDB caching, grid display
4. **Preview** — Full-image viewer with zoom
5. **SIFT in Worker** — opencv.js loading, single-image SIFT, verify output matches desktop
6. **Matching in Workers** — Pair comparison, score filtering, pair grid display
7. **Delete/Undo** — File operations via File System Access API
8. **IndexedDB caching** — Full cache (thumbnails + SIFT + similarity results)
9. **Polish** — Progress bars, error handling, browser compat check
10. **Deploy** — Azure Static Web Apps, compression, lazy loading

Each phase is independently testable. Phase 5 (SIFT worker) is the highest-risk item — prototype it early to validate that opencv.js SIFT output matches OpenCvSharp output.
