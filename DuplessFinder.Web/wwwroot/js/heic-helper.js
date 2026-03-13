// HEIC support:
//   1. EXIF IFD1 thumbnail extraction — parses ISOBMFF container to find JPEG thumbnail (<5ms).
//   2. libheif-js   — WASM-based full HEIC decode (~0.5-1.5s). For SIFT and preview.
//   3. Native createImageBitmap — used first; works on Android/macOS. Fastest path.

let libheifInstance = null;
let libheifLoading = null;

const heicExtensions = new Set(['heic', 'heif']);
const conversionCache = new Map(); // fileName → Promise<Blob>

export function isHeic(fileName) {
    if (!fileName) return false;
    const ext = fileName.split('.').pop().toLowerCase();
    return heicExtensions.has(ext);
}

export function isHeicBlob(blob) {
    return blob.type === 'image/heic' || blob.type === 'image/heif';
}

// --- libheif-js: WASM-based full HEIC decode ---

async function loadLibheif() {
    if (libheifInstance) return libheifInstance;
    if (libheifLoading) return libheifLoading;

    libheifLoading = (async () => {
        const mod = await import('../lib/libheif-bundle.mjs');
        const factory = mod.default || mod;
        libheifInstance = factory();
        console.log('[heic-helper] libheif-js WASM loaded.');
        return libheifInstance;
    })();

    return libheifLoading;
}

/**
 * Decode a libheif image handle to ImageBitmap using heif_js_decode_image2.
 * Works for both primary images and thumbnail handles.
 */
function decodeHandleToImageBitmap(libheif, handle) {
    const w = libheif.heif_image_handle_get_width(handle);
    const h = libheif.heif_image_handle_get_height(handle);

    return new Promise((resolve, reject) => {
        setTimeout(() => {
            try {
                const result = libheif.heif_js_decode_image2(
                    handle,
                    libheif.heif_colorspace.heif_colorspace_RGB,
                    libheif.heif_chroma.heif_chroma_interleaved_RGBA
                );

                if (!result || result.code) {
                    reject(new Error('HEIF decode failed'));
                    return;
                }

                const rgba = new Uint8ClampedArray(w * h * 4);
                for (const ch of result.channels) {
                    if (ch.id === libheif.heif_channel.heif_channel_interleaved) {
                        if (ch.stride === ch.width * 4) {
                            rgba.set(ch.data);
                        } else {
                            for (let y = 0; y < ch.height; y++) {
                                const row = ch.data.slice(y * ch.stride, y * ch.stride + ch.width * 4);
                                rgba.set(row, y * ch.width * 4);
                            }
                        }
                    }
                }

                libheif.heif_image_release(result.image);

                const imageData = new ImageData(rgba, w, h);
                createImageBitmap(imageData).then(resolve, reject);
            } catch (err) {
                reject(err);
            }
        }, 0);
    });
}

/**
 * Extract JPEG thumbnail from HEIC file's EXIF IFD1 data (~1-5ms).
 * Parses the ISOBMFF container to locate the Exif item, then extracts
 * the standard TIFF IFD1 JPEG thumbnail. No HEVC decoding needed.
 * Returns a Blob (image/jpeg) or null if no thumbnail found.
 */
export async function extractHeicThumbnail(blob) {
    try {
        const buffer = await blob.arrayBuffer();
        const view = new DataView(buffer);
        const bytes = new Uint8Array(buffer);

        // Step 1: Find the 'meta' box in the top-level ISOBMFF structure
        let metaOffset = -1, metaSize = 0;
        let offset = 0;
        while (offset < buffer.byteLength - 8) {
            let boxSize = view.getUint32(offset);
            const boxType = String.fromCharCode(bytes[offset+4], bytes[offset+5], bytes[offset+6], bytes[offset+7]);
            if (boxSize === 1 && offset + 16 <= buffer.byteLength) {
                // 64-bit extended size
                boxSize = Number(view.getBigUint64(offset + 8));
            }
            if (boxSize === 0) boxSize = buffer.byteLength - offset;
            if (boxSize < 8) break;
            if (boxType === 'meta') {
                metaOffset = offset;
                metaSize = boxSize;
                break;
            }
            offset += boxSize;
        }
        if (metaOffset < 0) return null;

        // Step 2: Inside 'meta', find 'iloc' and 'iinf' boxes to locate the Exif item
        // meta is a full box (4 bytes version+flags after header)
        const metaBodyStart = metaOffset + 12; // 8 (header) + 4 (fullbox)
        const metaEnd = metaOffset + metaSize;

        let ilocData = null, iinfData = null;
        offset = metaBodyStart;
        while (offset < metaEnd - 8) {
            let boxSize = view.getUint32(offset);
            const boxType = String.fromCharCode(bytes[offset+4], bytes[offset+5], bytes[offset+6], bytes[offset+7]);
            if (boxSize === 1 && offset + 16 <= buffer.byteLength) {
                boxSize = Number(view.getBigUint64(offset + 8));
            }
            if (boxSize === 0) boxSize = metaEnd - offset;
            if (boxSize < 8) break;
            if (boxType === 'iloc') ilocData = { offset: offset, size: boxSize };
            if (boxType === 'iinf') iinfData = { offset: offset, size: boxSize };
            if (ilocData && iinfData) break;
            offset += boxSize;
        }
        if (!ilocData || !iinfData) return null;

        // Step 3: Parse 'iinf' to find the item ID with type 'Exif'
        let exifItemId = -1;
        {
            const iinfStart = iinfData.offset + 8; // box header
            const iinfEnd = iinfData.offset + iinfData.size;
            const version = bytes[iinfStart];
            const entryCountOff = iinfStart + 4;
            const entryCount = version === 0
                ? view.getUint16(entryCountOff)
                : view.getUint32(entryCountOff);
            let pos = entryCountOff + (version === 0 ? 2 : 4);

            for (let i = 0; i < entryCount && pos < iinfEnd - 8; i++) {
                let infeSize = view.getUint32(pos);
                const infeType = String.fromCharCode(bytes[pos+4], bytes[pos+5], bytes[pos+6], bytes[pos+7]);
                if (infeSize < 8) break;
                if (infeType === 'infe') {
                    const infeStart = pos + 8; // after header
                    const infeVer = bytes[infeStart];
                    if (infeVer >= 2) {
                        const itemId = infeVer === 2
                            ? view.getUint16(infeStart + 4)
                            : view.getUint32(infeStart + 4);
                        // Item type is 4 bytes after itemId + 2 bytes protection index
                        const typeOff = infeStart + (infeVer === 2 ? 8 : 10);
                        if (typeOff + 4 <= pos + infeSize) {
                            const itemType = String.fromCharCode(bytes[typeOff], bytes[typeOff+1], bytes[typeOff+2], bytes[typeOff+3]);
                            if (itemType === 'Exif') {
                                exifItemId = itemId;
                                break;
                            }
                        }
                    }
                }
                pos += infeSize;
            }
        }
        if (exifItemId < 0) return null;

        // Step 4: Parse 'iloc' to find the file offset/length of the Exif item
        let exifOffset = -1, exifLength = 0;
        {
            const ilocStart = ilocData.offset + 8; // box header
            const ilocVer = bytes[ilocStart];
            const sizeByte1 = bytes[ilocStart + 4];
            const sizeByte2 = bytes[ilocStart + 5];
            const offsetSize = (sizeByte1 >> 4) & 0xF;
            const lengthSize = sizeByte1 & 0xF;
            const baseOffsetSize = (sizeByte2 >> 4) & 0xF;
            const indexSize = ilocVer >= 1 ? (sizeByte2 & 0xF) : 0;

            const itemCountOff = ilocStart + 6;
            const itemCount = ilocVer < 2
                ? view.getUint16(itemCountOff)
                : view.getUint32(itemCountOff);
            let pos = itemCountOff + (ilocVer < 2 ? 2 : 4);

            function readN(p, n) {
                if (n === 0) return 0;
                if (n === 2) return view.getUint16(p);
                if (n === 4) return view.getUint32(p);
                if (n === 8) return Number(view.getBigUint64(p));
                return 0;
            }

            for (let i = 0; i < itemCount; i++) {
                const itemId = ilocVer < 2 ? view.getUint16(pos) : view.getUint32(pos);
                pos += ilocVer < 2 ? 2 : 4;
                if (ilocVer >= 1) pos += 2; // construction_method
                pos += 2; // data_reference_index
                const baseOffset = readN(pos, baseOffsetSize);
                pos += baseOffsetSize;
                const extentCount = view.getUint16(pos);
                pos += 2;

                for (let e = 0; e < extentCount; e++) {
                    if (ilocVer >= 1 && indexSize > 0) pos += indexSize;
                    const extOffset = readN(pos, offsetSize);
                    pos += offsetSize;
                    const extLength = readN(pos, lengthSize);
                    pos += lengthSize;

                    if (itemId === exifItemId) {
                        exifOffset = baseOffset + extOffset;
                        exifLength = extLength;
                    }
                }
                if (exifOffset >= 0) break;
            }
        }
        if (exifOffset < 0 || exifLength === 0) return null;

        // Step 5: The Exif item starts with a 4-byte prefix (offset to TIFF header),
        // then "Exif\0\0", then the TIFF data
        const exifPrefixLen = view.getUint32(exifOffset);
        const tiffStart = exifOffset + 4 + exifPrefixLen;
        if (tiffStart + 8 > buffer.byteLength) return null;

        // Step 6: Parse TIFF header to find IFD1 with thumbnail
        const byteOrder = view.getUint16(tiffStart);
        const le = byteOrder === 0x4949; // 'II' = little-endian

        function getU16(off) { return view.getUint16(off, le); }
        function getU32(off) { return view.getUint32(off, le); }

        const ifd0Offset = getU32(tiffStart + 4);
        const ifd0Pos = tiffStart + ifd0Offset;

        // Skip IFD0 to get to IFD1
        const ifd0Count = getU16(ifd0Pos);
        const ifd1OffsetPos = ifd0Pos + 2 + ifd0Count * 12;
        const ifd1Offset = getU32(ifd1OffsetPos);
        if (ifd1Offset === 0) return null;

        const ifd1Pos = tiffStart + ifd1Offset;
        const ifd1Count = getU16(ifd1Pos);

        let thumbOffset = 0, thumbLength = 0, compression = 0;
        for (let i = 0; i < ifd1Count; i++) {
            const entryPos = ifd1Pos + 2 + i * 12;
            const tag = getU16(entryPos);
            if (tag === 0x0103) compression = getU16(entryPos + 8); // Compression
            if (tag === 0x0201) thumbOffset = getU32(entryPos + 8);  // ThumbnailOffset
            if (tag === 0x0202) thumbLength = getU32(entryPos + 8);  // ThumbnailLength
        }

        // Compression 6 = JPEG
        if (compression !== 6 || thumbOffset === 0 || thumbLength === 0) return null;

        const absThumbOffset = tiffStart + thumbOffset;
        if (absThumbOffset + thumbLength > buffer.byteLength) return null;

        const jpegBytes = new Uint8Array(buffer, absThumbOffset, thumbLength);
        return new Blob([jpegBytes], { type: 'image/jpeg' });
    } catch (err) {
        console.warn('[heic-helper] EXIF thumbnail extraction failed:', err.message);
        return null;
    }
}

/**
 * Decode a HEIC blob to ImageBitmap using libheif-js (WASM).
 * ~5-10x faster than heic2any (~0.5-1.5s vs 8-13s for 4000×1868).
 */
async function decodeHeicWithLibheif(blob) {
    const libheif = await loadLibheif();
    const buffer = await blob.arrayBuffer();

    const decoder = new libheif.HeifDecoder();
    const images = decoder.decode(new Uint8Array(buffer));

    if (!images || images.length === 0) {
        throw new Error('No images found in HEIC file');
    }

    const image = images[0];
    return await decodeHandleToImageBitmap(libheif, image.handle);
}

/**
 * Convert a HEIC blob to JPEG blob using libheif-js WASM.
 * Caches by fileName so repeated calls reuse the result.
 */
export async function convertHeicToJpeg(blob, fileName) {
    if (fileName && conversionCache.has(fileName)) {
        return conversionCache.get(fileName);
    }

    const promise = (async () => {
        try {
            const t0 = performance.now();
            const bitmap = await decodeHeicWithLibheif(blob);
            const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
            const ctx = canvas.getContext('2d');
            ctx.drawImage(bitmap, 0, 0);
            bitmap.close();
            const jpegBlob = await canvas.convertToBlob({ type: 'image/jpeg', quality: 0.92 });
            const elapsed = (performance.now() - t0).toFixed(0);
            console.log(`[heic-helper] libheif decoded ${fileName || 'HEIC'}: ${canvas.width}x${canvas.height} in ${elapsed}ms`);
            return jpegBlob;
        } catch (err) {
            console.warn('[heic-helper] libheif decode failed:', err.message);
            return blob;
        }
    })();

    if (fileName) {
        conversionCache.set(fileName, promise);
    }
    return promise;
}

/**
 * createImageBitmap with HEIC fallback via libheif-js WASM.
 * Tries native decode first (instant on Android/macOS).
 * Falls back to libheif-js when native fails (Windows Chrome).
 */
export async function createImageBitmapSafe(blob, fileName) {
    try {
        return await createImageBitmap(blob);
    } catch (e) {
        if (isHeic(fileName) || isHeicBlob(blob)) {
            console.log('[heic-helper] Native decode failed for HEIC, decoding via libheif-js...');
            return await decodeHeicWithLibheif(blob);
        }
        throw e;
    }
}

/**
 * Clear the conversion cache (call when loading new files).
 */
export function clearConversionCache() {
    conversionCache.clear();
}
