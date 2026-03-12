'use strict';

const SIFT_RESIZE_PX = 200;
const LOWES_RATIO = 0.7;

importScripts('../lib/opencv.js');

const cvReady = (async () => {
    if (typeof cv === 'function') {
        // MODULARIZE=1: cv is the async factory function after importScripts.
        // We must pass locateFile so the WASM file is found relative to lib/, not js/.
        const instance = await cv({
            locateFile: (path) => '../lib/' + path
        });
        self.cv = instance;
        console.log('[sift-worker] OpenCV WASM initialized. SIFT:', typeof instance.SIFT);
    } else if (typeof cv !== 'undefined' && typeof cv.then === 'function') {
        const instance = await cv;
        self.cv = instance;
        console.log('[sift-worker] OpenCV WASM initialized (promise).');
    } else if (typeof cv !== 'undefined' && cv.Mat) {
        console.log('[sift-worker] OpenCV runtime ready (immediate).');
    } else {
        throw new Error('[sift-worker] cv global not found after importScripts.');
    }
})();

// Uses createImageBitmap + OffscreenCanvas because cv.imdecode is not
// exposed in the standard opencv.js WASM bindings.
async function decodeAndResize(imageBytes, maxDim) {
    const blob = new Blob([imageBytes]);
    const bitmap = await createImageBitmap(blob);

    const w = bitmap.width;
    const h = bitmap.height;
    const scale = Math.min(maxDim / Math.max(h, w), 1.0);
    const newW = Math.round(w * scale);
    const newH = Math.round(h * scale);

    const canvas = new OffscreenCanvas(newW, newH);
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
async function computeSift(imageBytes, resizePx) {
    const maxDim = resizePx || SIFT_RESIZE_PX;
    let img = null;
    let gray = null;
    let sift = null;
    let keypoints = null;
    let descriptors = null;
    let mask = null;

    try {
        img = await decodeAndResize(imageBytes, maxDim);

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
function matchPair(desc1, desc2, lowesRatio) {
    const ratio = lowesRatio || LOWES_RATIO;
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
                if (m.distance < ratio * n.distance) {
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

self.onmessage = async (e) => {
    const { type, id } = e.data;

    try {
        await cvReady;

        if (type === 'computeSift') {
            const { imageBytes, fingerprint, resizePx } = e.data;
            const result = await computeSift(new Uint8Array(imageBytes), resizePx);
            self.postMessage(
                { type: 'computeSift', fingerprint, rows: result.rows, cols: result.cols, descriptorData: result.descriptorData },
                [result.descriptorData.buffer]
            );
        } else if (type === 'matchPair') {
            const { desc1, desc2, lowesRatio } = e.data;
            const score = matchPair(desc1, desc2, lowesRatio);
            self.postMessage({ type: 'matchPair', id, score });
        } else {
            self.postMessage({ error: `Unknown message type: ${type}` });
        }
    } catch (err) {
        console.error('[sift-worker] Error:', err);
        self.postMessage({ error: err.message || 'Worker error', type, id });
    }
};
