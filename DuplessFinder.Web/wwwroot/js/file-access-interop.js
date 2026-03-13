// Requires a Chromium-based browser with File System Access API support.

const supportedExtensions = new Set([
    'jpg', 'jpeg', 'png', 'bmp', 'tiff', 'tif', 'webp'
]);

const deletedFolder = '.deleted';

let rootDirHandle = null;
const pickedFiles = new Map(); // name -> File (from <input type="file">)

export function isFileSystemAccessSupported() {
    return 'showDirectoryPicker' in window;
}

export async function pickDirectory() {
    if (!isFileSystemAccessSupported()) {
        throw new Error(
            'File System Access API is not supported in this browser. ' +
            'Please use Chrome, Edge, or another Chromium-based browser.'
        );
    }

    try {
        rootDirHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
        console.log('[file-access] Directory selected:', rootDirHandle.name);
        return rootDirHandle.name;
    } catch (err) {
        if (err.name === 'AbortError') {
            console.log('[file-access] User cancelled directory picker.');
            return null;
        }
        console.error('[file-access] Error picking directory:', err);
        throw err;
    }
}

export function clickElement(element) {
    element.click();
}

export async function pickFiles(inputElement) {
    pickedFiles.clear();
    const results = [];

    for (const file of inputElement.files) {
        const ext = file.name.split('.').pop().toLowerCase();
        if (!supportedExtensions.has(ext)) continue;

        pickedFiles.set(file.name, file);
        results.push({
            path: file.name,
            name: file.name,
            size: file.size,
            lastModified: file.lastModified
        });
    }

    console.log(`[file-access] Picked ${results.length} image(s) via file input.`);

    // Reset input so re-selecting the same files triggers onchange again
    inputElement.value = '';

    return results;
}

export async function ensureDirectoryAccess() {
    if (rootDirHandle) return true;

    if (!('showDirectoryPicker' in window)) return false;

    try {
        rootDirHandle = await window.showDirectoryPicker({ mode: 'readwrite' });
        console.log('[file-access] Directory access granted:', rootDirHandle.name);
        return true;
    } catch (err) {
        if (err.name === 'AbortError') {
            console.log('[file-access] User cancelled directory picker.');
            return false;
        }
        console.error('[file-access] Error getting directory access:', err);
        return false;
    }
}

export async function resolvePathByFingerprint(fingerprint, fileName) {
    if (!rootDirHandle) return null;

    const results = [];
    await enumerateDir(rootDirHandle, '', true, results);

    // Prefer match by fingerprint + name
    for (const entry of results) {
        const fp = `${entry.size}_${entry.lastModified}`;
        if (fp === fingerprint && entry.name === fileName) return entry.path;
    }

    // Fallback: fingerprint only
    for (const entry of results) {
        const fp = `${entry.size}_${entry.lastModified}`;
        if (fp === fingerprint) return entry.path;
    }

    return null;
}

export async function scanImages(includeSubfolders) {
    if (!rootDirHandle) {
        throw new Error('[file-access] No directory selected. Call pickDirectory() first.');
    }

    const results = [];
    await enumerateDir(rootDirHandle, '', includeSubfolders, results);
    console.log(`[file-access] Scanned ${results.length} image(s).`);
    return results;
}

async function enumerateDir(dirHandle, prefix, recurse, results) {
    for await (const entry of dirHandle.values()) {
        const relativePath = prefix ? `${prefix}/${entry.name}` : entry.name;

        if (entry.kind === 'file') {
            const ext = entry.name.split('.').pop().toLowerCase();
            if (supportedExtensions.has(ext)) {
                try {
                    const file = await entry.getFile();
                    results.push({
                        path: relativePath,
                        name: entry.name,
                        size: file.size,
                        lastModified: file.lastModified
                    });
                } catch (err) {
                    console.warn(`[file-access] Could not read file ${relativePath}:`, err);
                }
            }
        } else if (entry.kind === 'directory' && recurse) {
            if (entry.name === deletedFolder) continue;
            await enumerateDir(entry, relativePath, recurse, results);
        }
    }
}

async function resolveFile(dirHandle, relativePath) {
    const segments = relativePath.split('/').filter(Boolean);
    let current = dirHandle;

    for (let i = 0; i < segments.length - 1; i++) {
        current = await current.getDirectoryHandle(segments[i]);
    }

    return current.getFileHandle(segments[segments.length - 1]);
}

export async function readFileBytes(relativePath) {
    // Check picked files first (from <input type="file">)
    const picked = pickedFiles.get(relativePath);
    if (picked) {
        const buffer = await picked.arrayBuffer();
        return new Uint8Array(buffer);
    }

    if (!rootDirHandle) {
        throw new Error('[file-access] No directory selected.');
    }

    try {
        const fileHandle = await resolveFile(rootDirHandle, relativePath);
        const file = await fileHandle.getFile();
        const buffer = await file.arrayBuffer();
        return new Uint8Array(buffer);
    } catch (err) {
        console.error(`[file-access] Error reading file ${relativePath}:`, err);
        throw err;
    }
}

export async function moveToDeleted(relativePath) {
    if (!rootDirHandle) {
        throw new Error('[file-access] No directory selected.');
    }

    try {
        const segments = relativePath.split('/').filter(Boolean);
        const fileName = segments[segments.length - 1];

        let parentDir = rootDirHandle;
        for (let i = 0; i < segments.length - 1; i++) {
            parentDir = await parentDir.getDirectoryHandle(segments[i]);
        }

        const deletedDir = await parentDir.getDirectoryHandle(deletedFolder, { create: true });

        const deletedName = await findUniqueName(deletedDir, fileName);

        const sourceHandle = await parentDir.getFileHandle(fileName);
        const sourceFile = await sourceHandle.getFile();

        const destHandle = await deletedDir.getFileHandle(deletedName, { create: true });
        const readable = sourceFile.stream();
        const writable = await destHandle.createWritable();
        await readable.pipeTo(writable);

        await parentDir.removeEntry(fileName);

        console.log(`[file-access] Moved ${relativePath} -> .deleted/${deletedName}`);
        return deletedName;
    } catch (err) {
        console.error(`[file-access] Error moving ${relativePath} to .deleted:`, err);
        throw err;
    }
}

async function findUniqueName(dirHandle, desiredName) {
    let candidate = desiredName;
    let counter = 1;
    const dotIdx = desiredName.lastIndexOf('.');
    const baseName = dotIdx > 0 ? desiredName.substring(0, dotIdx) : desiredName;
    const ext = dotIdx > 0 ? desiredName.substring(dotIdx) : '';

    const maxAttempts = 1000;
    while (counter <= maxAttempts) {
        try {
            await dirHandle.getFileHandle(candidate);
            candidate = `${baseName} (${counter})${ext}`;
            counter++;
        } catch (e) {
            return candidate;
        }
    }
    throw new Error(`[file-access] Could not find a unique name for "${desiredName}" after ${MAX_ATTEMPTS} attempts.`);
}

export async function restoreFromDeleted(originalPath, deletedName) {
    if (!rootDirHandle) {
        throw new Error('[file-access] No directory selected.');
    }

    try {
        const segments = originalPath.split('/').filter(Boolean);
        const fileName = segments[segments.length - 1];

        let parentDir = rootDirHandle;
        for (let i = 0; i < segments.length - 1; i++) {
            parentDir = await parentDir.getDirectoryHandle(segments[i]);
        }

        const deletedDir = await parentDir.getDirectoryHandle(deletedFolder);

        const deletedHandle = await deletedDir.getFileHandle(deletedName);
        const deletedFile = await deletedHandle.getFile();
        const contents = await deletedFile.arrayBuffer();

        const restoredHandle = await parentDir.getFileHandle(fileName, { create: true });
        const writable = await restoredHandle.createWritable();
        await writable.write(contents);
        await writable.close();

        await deletedDir.removeEntry(deletedName);

        console.log(`[file-access] Restored ${deletedName} -> ${originalPath}`);
    } catch (err) {
        console.error(`[file-access] Error restoring ${deletedName} to ${originalPath}:`, err);
        throw err;
    }
}

export async function createObjectUrl(relativePath) {
    // Check picked files first (from <input type="file">)
    const picked = pickedFiles.get(relativePath);
    if (picked) {
        return URL.createObjectURL(picked);
    }

    if (!rootDirHandle) {
        throw new Error('[file-access] No directory selected.');
    }

    try {
        const fileHandle = await resolveFile(rootDirHandle, relativePath);
        const file = await fileHandle.getFile();
        const url = URL.createObjectURL(file);
        return url;
    } catch (err) {
        console.error(`[file-access] Error creating object URL for ${relativePath}:`, err);
        throw err;
    }
}

export function revokeObjectUrl(url) {
    try {
        URL.revokeObjectURL(url);
    } catch (err) {
        console.warn('[file-access] Error revoking object URL:', err);
    }
}

export async function deleteFile(relativePath) {
    if (!rootDirHandle) {
        throw new Error('[file-access] No directory selected.');
    }

    try {
        const segments = relativePath.split('/').filter(Boolean);
        const fileName = segments[segments.length - 1];

        let parentDir = rootDirHandle;
        for (let i = 0; i < segments.length - 1; i++) {
            parentDir = await parentDir.getDirectoryHandle(segments[i]);
        }

        await parentDir.removeEntry(fileName);
        console.log(`[file-access] Deleted ${relativePath}`);
    } catch (err) {
        console.error(`[file-access] Error deleting ${relativePath}:`, err);
        throw err;
    }
}
