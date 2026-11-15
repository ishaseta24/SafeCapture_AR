import sharp from 'sharp';
import { HttpError } from './photo-upload.js';

const MAX_PHOTOS = 20;
const MAX_BYTES = 64 * 1024 * 1024;
const MAX_THUMBNAIL_BYTES = 64 * 1024;

// Input MUST be the completed privacy render, never the camera JPEG.
// Awaited inside the existing single-photo admission, with no background queue.
export async function processedThumbnail(jpeg, maxPixels) {
  try {
    const thumbnail = await sharp(jpeg, { limitInputPixels: maxPixels, failOn: 'warning' })
      .resize({ width: 240, height: 240, fit: 'inside', withoutEnlargement: true })
      .jpeg({ quality: 60 }).toBuffer();
    return thumbnail.length && thumbnail.length <= MAX_THUMBNAIL_BYTES ? thumbnail : null;
  } catch {
    // A missing thumbnail must not discard a safe full image or fall back to raw.
    return null;
  }
}

export class PhotoHistory {
  constructor() {
    this.entries = new Map();
    this.bytes = 0;
    this.revision = 0;
  }

  invalidate(frameId) {
    const entry = this.entries.get(frameId);
    if (!entry) return;
    this.bytes -= entry.jpeg.length + (entry.thumbnail?.length ?? 0);
    this.entries.delete(frameId);
    this.revision += 1;
  }

  publish(jpeg, thumbnail, metadata) {
    this.invalidate(metadata.frame_id);
    const size = jpeg.length + (thumbnail?.length ?? 0);
    if (size > MAX_BYTES) return;
    while (this.entries.size >= MAX_PHOTOS || this.bytes + size > MAX_BYTES) {
      this.invalidate(this.entries.keys().next().value);
    }
    // Only safe scalars are retained beside already-processed image buffers.
    const photo = {
      frame_id: metadata.frame_id, captured_at: metadata.captured_at,
      revision: metadata.revision, width: metadata.width, height: metadata.height,
      selected_count: metadata.selected_count, manual_mask_count: metadata.manual_mask_count,
      auto_mask_count: metadata.auto_mask_count, fail_closed: metadata.fail_closed,
      thumbnail_available: !!thumbnail,
    };
    this.entries.set(photo.frame_id, { photo, jpeg, thumbnail });
    this.bytes += size;
    this.revision += 1;
  }

  image(frameId, revision, thumbnail = false) {
    const entry = this.entries.get(frameId);
    if (!entry) throw new HttpError(404, 'Photo unavailable: evicted, discarded, or withheld');
    if (typeof revision !== 'string' || revision !== String(entry.photo.revision)) {
      throw new HttpError(409, 'Photo revision changed or missing; refresh gallery metadata');
    }
    const jpeg = thumbnail ? entry.thumbnail : entry.jpeg;
    if (!jpeg) throw new HttpError(404, 'Thumbnail unavailable; the processed full photo is still available');
    return jpeg;
  }

  status() {
    return { limit: MAX_PHOTOS, max_bytes: MAX_BYTES, bytes_used: this.bytes,
      revision: this.revision,
      photos: [...this.entries.values()].reverse().map(({ photo }) => ({ ...photo })) };
  }

  clear() {
    this.entries.clear();
    this.bytes = 0;
    this.revision += 1;
  }
}
