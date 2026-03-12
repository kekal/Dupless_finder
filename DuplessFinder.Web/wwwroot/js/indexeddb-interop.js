const dbName = 'dupless_cache';
const dbVersion = 1;

const storeThumbnails = 'thumbnails';
const storeSift = 'sift_descriptors';
const storeSimilarityResults = 'similarity_results';

let db = null;
let quotaExceeded = false;

export async function openDatabase() {
    if (db) return;

    return new Promise((resolve, reject) => {
        const request = indexedDB.open(dbName, dbVersion);

        request.onupgradeneeded = (event) => {
            const database = event.target.result;
            console.log('[indexeddb] Upgrading database to version', dbVersion);

            if (!database.objectStoreNames.contains(storeThumbnails)) {
                database.createObjectStore(storeThumbnails, { keyPath: 'fingerprint' });
                console.log('[indexeddb] Created store:', storeThumbnails);
            }

            if (!database.objectStoreNames.contains(storeSift)) {
                database.createObjectStore(storeSift, { keyPath: 'fingerprint' });
                console.log('[indexeddb] Created store:', storeSift);
            }

            if (!database.objectStoreNames.contains(storeSimilarityResults)) {
                // Compound key [fp1, fp2] with secondary index on score for sorted queries
                const store = database.createObjectStore(storeSimilarityResults, {
                    keyPath: ['fingerprint1', 'fingerprint2']
                });
                store.createIndex('score', 'score', { unique: false });
                console.log('[indexeddb] Created store:', storeSimilarityResults);
            }
        };

        request.onsuccess = (event) => {
            db = event.target.result;
            db.onerror = (e) => {
                console.error('[indexeddb] Database error:', e.target.error);
            };
            db.onclose = () => {
                console.warn('[indexeddb] Database was closed externally.');
                db = null;
            };
            console.log('[indexeddb] Database opened successfully.');
            resolve();
        };

        request.onerror = (event) => {
            console.error('[indexeddb] Error opening database:', event.target.error);
            reject(event.target.error);
        };
    });
}

async function ensureDb() {
    if (!db) {
        await openDatabase();
    }
}

function promisifyRequest(request) {
    return new Promise((resolve, reject) => {
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function promisifyTransaction(tx) {
    return new Promise((resolve, reject) => {
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
        tx.onabort = () => reject(tx.error || new Error('Transaction aborted'));
    });
}

export async function getThumbnail(fingerprint) {
    await ensureDb();
    try {
        const tx = db.transaction(storeThumbnails, 'readonly');
        const store = tx.objectStore(storeThumbnails);
        const result = await promisifyRequest(store.get(fingerprint));
        return result ? result.jpegBytes : null;
    } catch (err) {
        console.error('[indexeddb] Error getting thumbnail:', err);
        throw err;
    }
}

export async function putThumbnail(fingerprint, jpegBytes) {
    await ensureDb();
    try {
        const tx = db.transaction(storeThumbnails, 'readwrite');
        const store = tx.objectStore(storeThumbnails);
        store.put({ fingerprint, jpegBytes });
        await promisifyTransaction(tx);
    } catch (err) {
        if (err.name === 'QuotaExceededError') {
            quotaExceeded = true;
            console.error('[indexeddb] QuotaExceededError putting thumbnail.');
        }
        console.error('[indexeddb] Error putting thumbnail:', err);
        throw err;
    }
}

export async function putThumbnailBatch(items) {
    await ensureDb();
    try {
        const tx = db.transaction(storeThumbnails, 'readwrite');
        const store = tx.objectStore(storeThumbnails);
        for (const item of items) {
            store.put({ fingerprint: item.fingerprint, jpegBytes: item.jpegBytes });
        }
        await promisifyTransaction(tx);
        console.log(`[indexeddb] Stored ${items.length} thumbnail(s) in batch.`);
    } catch (err) {
        if (err.name === 'QuotaExceededError') {
            quotaExceeded = true;
            console.error('[indexeddb] QuotaExceededError in putThumbnailBatch.');
        }
        console.error('[indexeddb] Error in putThumbnailBatch:', err);
        throw err;
    }
}

export async function getSiftDescriptor(fingerprint) {
    await ensureDb();
    try {
        const tx = db.transaction(storeSift, 'readonly');
        const store = tx.objectStore(storeSift);
        const result = await promisifyRequest(store.get(fingerprint));
        if (!result) return null;
        return { rows: result.rows, cols: result.cols, data: result.data };
    } catch (err) {
        console.error('[indexeddb] Error getting SIFT descriptor:', err);
        throw err;
    }
}

export async function putSiftDescriptor(fingerprint, data, rows, cols) {
    await ensureDb();
    try {
        const tx = db.transaction(storeSift, 'readwrite');
        const store = tx.objectStore(storeSift);
        store.put({ fingerprint, data, rows, cols });
        await promisifyTransaction(tx);
    } catch (err) {
        if (err.name === 'QuotaExceededError') {
            quotaExceeded = true;
            console.error('[indexeddb] QuotaExceededError putting SIFT descriptor.');
        }
        console.error('[indexeddb] Error putting SIFT descriptor:', err);
        throw err;
    }
}

// Normalize fingerprint ordering so (A,B) and (B,A) map to the same compound key
function normalizeOrder(fp1, fp2) {
    return fp1 <= fp2 ? [fp1, fp2] : [fp2, fp1];
}

export async function getAllSimilarityResults() {
    await ensureDb();
    try {
        const tx = db.transaction(storeSimilarityResults, 'readonly');
        const store = tx.objectStore(storeSimilarityResults);
        const results = await promisifyRequest(store.getAll());
        return results || [];
    } catch (err) {
        console.error('[indexeddb] Error getting similarity results:', err);
        throw err;
    }
}

export async function storeSimilarity(fingerprint1, fingerprint2, score) {
    await ensureDb();
    const [fp1, fp2] = normalizeOrder(fingerprint1, fingerprint2);
    try {
        const tx = db.transaction(storeSimilarityResults, 'readwrite');
        const store = tx.objectStore(storeSimilarityResults);
        store.put({ fingerprint1: fp1, fingerprint2: fp2, score });
        await promisifyTransaction(tx);
    } catch (err) {
        if (err.name === 'QuotaExceededError') {
            quotaExceeded = true;
            console.error('[indexeddb] QuotaExceededError storing similarity.');
        }
        console.error('[indexeddb] Error storing similarity:', err);
        throw err;
    }
}

export async function storeSimilarityBatch(entries) {
    await ensureDb();
    try {
        const tx = db.transaction(storeSimilarityResults, 'readwrite');
        const store = tx.objectStore(storeSimilarityResults);
        for (const entry of entries) {
            const [fp1, fp2] = normalizeOrder(entry.fingerprint1, entry.fingerprint2);
            store.put({ fingerprint1: fp1, fingerprint2: fp2, score: entry.score });
        }
        await promisifyTransaction(tx);
        console.log(`[indexeddb] Stored ${entries.length} similarity result(s) in batch.`);
    } catch (err) {
        if (err.name === 'QuotaExceededError') {
            quotaExceeded = true;
            console.error('[indexeddb] QuotaExceededError in storeSimilarityBatch.');
        }
        console.error('[indexeddb] Error in storeSimilarityBatch:', err);
        throw err;
    }
}

// Request persistent storage so the browser doesn't evict IndexedDB data under storage pressure
export async function requestPersistentStorage() {
    if (navigator.storage && navigator.storage.persist) {
        const granted = await navigator.storage.persist();
        console.log(`[indexeddb] Persistent storage ${granted ? 'granted' : 'denied'}.`);
        return granted;
    }
    console.warn('[indexeddb] navigator.storage.persist() not available.');
    return false;
}

export function isQuotaExceeded() {
    return quotaExceeded;
}

export async function clearCache() {
    await ensureDb();
    try {
        const storeNames = [storeThumbnails, storeSift, storeSimilarityResults];
        const tx = db.transaction(storeNames, 'readwrite');
        for (const name of storeNames) {
            tx.objectStore(name).clear();
        }
        await promisifyTransaction(tx);
        quotaExceeded = false;
        console.log('[indexeddb] All stores cleared.');
    } catch (err) {
        console.error('[indexeddb] Error clearing cache:', err);
        throw err;
    }
}

export async function getCacheSize() {
    await ensureDb();
    try {
        const storeNames = [storeThumbnails, storeSift, storeSimilarityResults];
        const tx = db.transaction(storeNames, 'readonly');
        const counts = {};
        for (const name of storeNames) {
            counts[name] = await promisifyRequest(tx.objectStore(name).count());
        }
        return counts;
    } catch (err) {
        console.error('[indexeddb] Error getting cache size:', err);
        throw err;
    }
}

export async function clearSimilarityResults() {
    await ensureDb();
    try {
        const tx = db.transaction(storeSimilarityResults, 'readwrite');
        tx.objectStore(storeSimilarityResults).clear();
        await promisifyTransaction(tx);
        console.log('[indexeddb] Similarity results cleared.');
    } catch (err) {
        console.error('[indexeddb] Error clearing similarity results:', err);
        throw err;
    }
}
