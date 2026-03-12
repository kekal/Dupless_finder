import { WorkerPool } from './worker-pool.js';

const siftResizePx = 200;
const lowesRatio = 0.7;

let cvLoaded = false;
let cvLoadPromise = null;
let workerPool = null;
const descriptorCache = new Map();

export async function ensureLoaded() {
    if (cvLoaded) return;
    if (cvLoadPromise) {
        await cvLoadPromise;
        return;
    }

    cvLoadPromise = (async () => {
        if (typeof cv !== 'undefined' && cv.Mat) {
            cvLoaded = true;
            console.log('[opencv-interop] OpenCV already loaded.');
            return;
        }

        // The opencv.js UMD wrapper checks `typeof Module !== 'undefined'` and
        // passes it to cv(Module). We must set Module.locateFile BEFORE loading
        // the script so the .wasm file is resolved from /lib/ instead of root.
        window.__duplessOpenCvModule = {
            locateFile: (path) => {
                console.log('[opencv-interop] locateFile:', path);
                return './lib/' + path;
            }
        };
        // opencv.js checks for window.Module — alias it during load
        window.Module = window.__duplessOpenCvModule;

        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = './lib/opencv.js';
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => reject(new Error('[opencv-interop] Failed to load opencv.js script.'));
            document.head.appendChild(script);
        });

        if (window.Module === window.__duplessOpenCvModule) {
            delete window.Module;
        }

        console.log('[opencv-interop] opencv.js script loaded, window.cv typeof =', typeof window.cv);

        const rawCv = window.cv;

        if (typeof rawCv === 'function') {
            console.log('[opencv-interop] Calling cv factory...');
            // MODULARIZE=1: rawCv is the async factory — call it to init WASM
            const instance = await rawCv();
            window.cv = instance;
            cvLoaded = true;
            console.log('[opencv-interop] OpenCV WASM initialized (factory). SIFT available:', typeof instance.SIFT);
        } else if (rawCv && typeof rawCv.then === 'function') {
            console.log('[opencv-interop] Awaiting cv Promise...');
            const instance = await rawCv;
            window.cv = instance;
            cvLoaded = true;
            console.log('[opencv-interop] OpenCV WASM initialized (promise).');
        } else if (rawCv && rawCv.Mat) {
            cvLoaded = true;
            console.log('[opencv-interop] OpenCV runtime ready (immediate).');
        } else {
            throw new Error('[opencv-interop] Unrecognized cv type: ' + typeof rawCv);
        }
    })();

    try {
        await cvLoadPromise;
    } catch (err) {
        cvLoadPromise = null;
        throw err;
    }
}

// Uses createImageBitmap + OffscreenCanvas because cv.imdecode is not
// exposed in the standard opencv.js WASM bindings.
async function decodeAndResize(imageBytes, maxDim) {
    const blob = new Blob([imageBytes]);
    const bitmap = await window.createImageBitmap(blob);

    const w = bitmap.width;
    const h = bitmap.height;
    const scale = Math.min(maxDim / Math.max(h, w), 1.0);
    const newW = Math.round(w * scale);
    const newH = Math.round(h * scale);

    const canvas = new window.OffscreenCanvas(newW, newH);
    const ctx = canvas.getContext('2d');
    ctx.drawImage(bitmap, 0, 0, newW, newH);
    bitmap.close();

    const imageData = ctx.getImageData(0, 0, newW, newH);
    return cv.matFromImageData(imageData);
}

function matFromFloat32(data, rows, cols) {
    // If data is a Uint8Array (byte[] from C#), view it as Float32Array
    let floats;
    if (data instanceof Uint8Array) {
        floats = new Float32Array(data.buffer, data.byteOffset, data.byteLength / 4);
    } else if (data instanceof Float32Array) {
        floats = data;
    } else {
        floats = new Float32Array(data);
    }

    // Build mat and set data directly — avoids expensive Array.from() conversion
    const mat = new cv.Mat(rows, cols, cv.CV_32F);
    mat.data32F.set(floats);
    return mat;
}

// Returns descriptorData as Uint8Array (Float32 bytes) so Blazor marshals it as byte[]
export async function computeSift(imageBytes) {
    await ensureLoaded();

    let img = null;
    let gray = null;
    let sift = null;
    let keypoints = null;
    let descriptors = null;
    let mask = null;

    try {
        img = await decodeAndResize(imageBytes, siftResizePx);

        gray = new cv.Mat();
        cv.cvtColor(img, gray, cv.COLOR_RGBA2GRAY);

        sift = new cv.SIFT();
        keypoints = new cv.KeyPointVector();
        descriptors = new cv.Mat();
        mask = new cv.Mat();
        sift.detectAndCompute(gray, mask, keypoints, descriptors);

        const rows = descriptors.rows;
        const cols = descriptors.cols;
        const float32 = new Float32Array(descriptors.data32F);
        const descriptorData = new Uint8Array(float32.buffer.slice(0));

        return { rows, cols, descriptorData };
    } finally {
        if (mask) mask.delete();
        if (descriptors) descriptors.delete();
        if (keypoints) keypoints.delete();
        if (sift) sift.delete();
        if (gray) gray.delete();
        if (img) img.delete();
    }
}

// BFMatcher + Lowe's ratio test. Score = 1000/goodMatches (lower = more similar).
export async function matchPair(desc1, desc2) {
    await ensureLoaded();

    let mat1 = null;
    let mat2 = null;
    let bf = null;
    let matches = null;

    try {
        mat1 = matFromFloat32(desc1.data, desc1.rows, desc1.cols);
        mat2 = matFromFloat32(desc2.data, desc2.rows, desc2.cols);

        if (mat1.rows < 2 || mat2.rows < 2) {
            return Number.MAX_VALUE;
        }

        bf = new cv.BFMatcher(cv.NORM_L2);
        matches = new cv.DMatchVectorVector();
        bf.knnMatch(mat1, mat2, matches, 2);

        let goodCount = 0;
        for (let i = 0; i < matches.size(); i++) {
            const match = matches.get(i);
            if (match.size() >= 2) {
                const m = match.get(0);
                const n = match.get(1);
                if (m.distance < lowesRatio * n.distance) {
                    goodCount++;
                }
            }
        }

        return goodCount > 0 ? 1000.0 / goodCount : Number.MAX_VALUE;
    } finally {
        if (matches) matches.delete();
        if (bf) bf.delete();
        if (mat2) mat2.delete();
        if (mat1) mat1.delete();
    }
}

export async function generateThumbnail(imageBytes, maxSize) {
    const blob = new Blob([imageBytes]);
    const bitmap = await window.createImageBitmap(blob);

    const w = bitmap.width;
    const h = bitmap.height;
    const scale = Math.min(maxSize / Math.max(w, h), 1.0);
    const newW = Math.round(w * scale);
    const newH = Math.round(h * scale);

    const canvas = new window.OffscreenCanvas(newW, newH);
    const ctx = canvas.getContext('2d');
    ctx.drawImage(bitmap, 0, 0, newW, newH);
    bitmap.close();

    const jpegBlob = await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.85 });
    const buffer = await jpegBlob.arrayBuffer();
    return new Uint8Array(buffer);
}

export function initWorkerPool(poolSize) {
    if (workerPool) {
        console.warn('[opencv-interop] Worker pool already initialized.');
        return;
    }

    const size = poolSize || navigator.hardwareConcurrency || 4;
    workerPool = new WorkerPool('./js/sift-worker.js', size);
    console.log(`[opencv-interop] Worker pool initialized with ${size} worker(s).`);
}

export function terminateWorkerPool() {
    if (workerPool) {
        workerPool.terminate();
        workerPool = null;
        console.log('[opencv-interop] Worker pool terminated.');
    }
}

export async function computeSiftInWorker(imageBytes, fingerprint) {
    if (!workerPool) {
        initWorkerPool();
    }

    if (descriptorCache.has(fingerprint)) {
        const cached = descriptorCache.get(fingerprint);
        return { fingerprint, rows: cached.rows, cols: cached.cols, descriptorData: cached.descriptorData };
    }

    const buffer = imageBytes.buffer.slice(
        imageBytes.byteOffset,
        imageBytes.byteOffset + imageBytes.byteLength
    );

    const result = await workerPool.execute(
        { type: 'computeSift', imageBytes: buffer, fingerprint },
        [buffer]
    );

    const output = {
        fingerprint: result.fingerprint,
        rows: result.rows,
        cols: result.cols,
        descriptorData: result.descriptorData
    };

    descriptorCache.set(fingerprint, { rows: output.rows, cols: output.cols, descriptorData: output.descriptorData });

    return output;
}

export function clearDescriptorCache() {
    descriptorCache.clear();
    console.log('[opencv-interop] Descriptor cache cleared.');
}

export async function matchPairInWorker(desc1, desc2) {
    if (!workerPool) {
        initWorkerPool();
    }

    function toArrayBuffer(data) {
        // Ensure data is an ArrayBuffer for transfer.
        // C# may pass Uint8Array (byte[]) — get underlying buffer.
        if (data instanceof Uint8Array) {
            return data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);
        }
        if (data instanceof ArrayBuffer) {
            return data.slice(0);
        }
        return data.slice(0);
    }

    const data1 = toArrayBuffer(desc1.data);
    const data2 = toArrayBuffer(desc2.data);

    const result = await workerPool.execute(
        {
            type: 'matchPair',
            desc1: { data: data1, rows: desc1.rows, cols: desc1.cols },
            desc2: { data: data2, rows: desc2.rows, cols: desc2.cols }
        },
        [data1, data2]
    );

    return result.score;
}

export async function matchPairByFingerprint(fp1, fp2) {
    const desc1 = descriptorCache.get(fp1);
    const desc2 = descriptorCache.get(fp2);

    if (!desc1) throw new Error(`[opencv-interop] No cached descriptors for fingerprint: ${fp1}`);
    if (!desc2) throw new Error(`[opencv-interop] No cached descriptors for fingerprint: ${fp2}`);

    return matchPairInWorker(
        { data: desc1.descriptorData, rows: desc1.rows, cols: desc1.cols },
        { data: desc2.descriptorData, rows: desc2.rows, cols: desc2.cols }
    );
}

export async function matchPairsBatch(pairs) {
    const promises = pairs.map(({ fp1, fp2 }) => matchPairByFingerprint(fp1, fp2));
    return Promise.all(promises);
}
