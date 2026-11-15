// Snapshot adaptation of the copied Ray-Ban worker: no raw queue, fail closed.
import { randomUUID } from 'node:crypto';
import { VisionObjectTracker, validBox } from './postprocess.js';
import { renderPrivacyFrame } from './privacy.js';
import { HttpError } from './photo-upload.js';
import { PhotoHistory, processedThumbnail } from './photo-history.js';

export class PhotoPipeline {
  constructor({ detector, renderer = renderPrivacyFrame, now = Date.now, staleMs = 15000,
    maxPixels = 12000000, maxOutputBytes = 5242880, autoMaskClasses = [],
    onPublish = () => {}, onWithhold = () => {} }) {
    if (!Array.isArray(autoMaskClasses) || autoMaskClasses.some((label) => !['tv', 'laptop'].includes(label))) {
      throw new Error('Automatic mask classes must contain only tv or laptop');
    }
    Object.assign(this, { detector, renderer, now, staleMs, maxPixels, maxOutputBytes, onPublish, onWithhold });
    this.tracker = new VisionObjectTracker();
    this.autoMaskClasses = new Set(autoMaskClasses);
    this.autoSelected = new Map();
    // Only manual selections persist across source photos and trigger lost-track fallback.
    this.selected = new Map();
    this.detections = [];
    this.fullMask = false;
    this.received = 0;
    this.published = 0;
    this.lastInferenceAt = null;
    this.lastPhotoAt = null;
    this.error = null;
    this.frame = null;
    this.frameId = null;
    this.output = null;
    this.history = new PhotoHistory();
    this.closed = false;
  }

  withhold() {
    this.history.invalidate(this.frameId);
    this.output = null;
    this.onWithhold();
  }

  effectiveSelected() {
    return new Map([...this.selected, ...this.autoSelected]);
  }

  selectionStatus() {
    const selected = this.effectiveSelected();
    return { selected_track_ids: [...selected.keys()], selectedCount: selected.size,
      manual_selected_track_ids: [...this.selected.keys()],
      auto_mask_classes: [...this.autoMaskClasses], auto_selected_track_ids: [...this.autoSelected.keys()] };
  }

  async render(signal) {
    const frameId = this.frameId;
    const width = this.frame.width;
    const height = this.frame.height;
    this.withhold();
    try {
      const jpeg = await this.renderer(this.frame.jpeg, this.detections, this.effectiveSelected(),
        this.fullMask, this.maxPixels);
      if (signal?.aborted) throw new Error('Aborted');
      if (!Buffer.isBuffer(jpeg) || !jpeg.length || jpeg.length > this.maxOutputBytes) {
        throw new Error('Output limit');
      }
      const thumbnail = await processedThumbnail(jpeg, this.maxPixels);
      if (signal?.aborted || this.closed || frameId !== this.frameId) throw new Error('Photo changed');
      const revision = this.published + 1; // Never reset when an entry is invalidated.
      this.history.publish(jpeg, thumbnail, {
        frame_id: frameId, captured_at: this.lastPhotoAt, revision, width, height,
        selected_count: this.effectiveSelected().size, manual_mask_count: this.selected.size,
        auto_mask_count: this.autoSelected.size, fail_closed: this.fullMask,
      });
      this.output = jpeg;
      this.published = revision;
      this.onPublish(jpeg);
    } catch {
      if (frameId === this.frameId) {
        this.fullMask = true;
        this.error = 'Rendering failed or request aborted; output withheld';
        this.withhold();
      }
      throw new HttpError(503, 'Rendering failed or request aborted; output withheld');
    }
  }

  async process(frame, requestId, signal) {
    if (this.closed) throw new HttpError(503, 'Receiver is closing');
    const startedAt = this.now();
    this.received += 1;
    const expired = this.lastInferenceAt !== null && startedAt - this.lastInferenceAt > this.staleMs;
    const resized = this.frame && (this.frame.width !== frame.width || this.frame.height !== frame.height);
    if (expired || resized) {
      this.tracker.reset();
      if (this.selected.size) this.fullMask = true;
    }
    this.frame = frame; // At most one bounded raw JPEG, memory only.
    this.frameId = randomUUID();
    const frameId = this.frameId;
    this.autoSelected.clear(); // Automatic masks belong only to this source photo.
    this.lastPhotoAt = startedAt;
    this.withhold();
    try {
      const detections = await this.detector.detect(frame.jpeg, frame.width, frame.height);
      if (this.closed || frameId !== this.frameId) throw new Error('Photo changed');
      if (signal?.aborted) throw new Error('Aborted');
      if (!Array.isArray(detections) || detections.length > 100
        || detections.some((d) => !validBox(d.box, frame.width, frame.height)
          || typeof d.label !== 'string' || d.label.length > 80 || !Number.isFinite(d.confidence))) {
        throw new Error('Invalid detections');
      }
      this.detections = this.tracker.update(detections);
      for (const detection of this.detections) {
        if (this.autoMaskClasses.has(detection.label)) {
          this.autoSelected.set(detection.track_id, {
            label: detection.label, box: { ...detection.box },
            sourceWidth: frame.width, sourceHeight: frame.height,
          });
        }
      }
      this.lastInferenceAt = this.now();
      this.latencyMs = this.lastInferenceAt - startedAt;
      if (this.latencyMs > this.staleMs) this.fullMask = true;
      for (const [id, item] of this.selected) {
        const current = this.detections.find((d) => d.track_id === id);
        if (current && !resized) {
          Object.assign(item, { box: { ...current.box }, sourceWidth: frame.width, sourceHeight: frame.height });
        } else {
          // Retain the selected region AND latch opaque output on the first miss.
          this.fullMask = true;
        }
      }
      this.error = null;
    } catch {
      if (this.closed || frameId !== this.frameId) throw new HttpError(503, 'Photo changed or receiver closing');
      this.fullMask = true;
      this.error = 'Detection failed; full-frame mask required';
      this.detections = [];
      this.autoSelected.clear();
      this.tracker.reset();
    }
    await this.render(signal);
    return { requestId, status: 'processed', width: frame.width, height: frame.height,
      failClosed: this.fullMask, selectedCount: this.effectiveSelected().size };
  }

  async select(x, y, expectedFrameId) {
    if (!this.frameId || expectedFrameId !== this.frameId) {
      throw new HttpError(409, 'Photo changed or frame identity missing; refresh the photo and click again');
    }
    if (!this.frame || this.lastInferenceAt === null
      || this.error || this.fullMask
      || !Number.isFinite(x) || !Number.isFinite(y) || x < 0 || x > 1 || y < 0 || y > 1) {
      throw new HttpError(400, 'No selectable detection; check processing status or capture again');
    }
    const px = x * this.frame.width;
    const py = y * this.frame.height;
    const best = this.detections.filter(({ box: b }) => px >= b.x1 && px <= b.x2 && py >= b.y1 && py <= b.y2)
      .sort((a, b) => (a.box.x2 - a.box.x1) * (a.box.y2 - a.box.y1)
        - (b.box.x2 - b.box.x1) * (b.box.y2 - b.box.y1))[0];
    if (!best) throw new HttpError(400, 'No detected object at that point');
    if (this.autoSelected.has(best.track_id)) {
      throw new HttpError(409, `Automatic ${best.label} mask is required by policy and cannot be toggled`);
    }
    if (this.selected.has(best.track_id)) this.selected.delete(best.track_id);
    else this.selected.set(best.track_id, {
      label: best.label, box: { ...best.box }, sourceWidth: this.frame.width, sourceHeight: this.frame.height,
    });
    await this.render();
  }

  clear() {
    // Clear manual state and the photo, never the automatic policy or republish old/raw output.
    this.withhold(); // Invalidate the correct history entry BEFORE clearing its identity.
    this.selected.clear();
    this.autoSelected.clear();
    this.fullMask = false;
    this.detections = [];
    this.tracker.reset();
    this.frame = null;
    this.frameId = null;
    this.lastPhotoAt = null;
    this.lastInferenceAt = null;
    this.error = null;
  }

  close() {
    this.closed = true;
    this.clear();
    this.history.clear();
  }

  status() {
    return { frame_id: this.frameId, frames_received: this.received, frames_published: this.published,
      detection_latency_ms: this.latencyMs ?? null, detection_error: this.error,
      objects: this.detections, ...this.selectionStatus(),
      fail_closed: this.fullMask, output_available: !!this.output,
      stale: this.lastPhotoAt === null || this.now() - this.lastPhotoAt > this.staleMs,
      last_photo_at: this.lastPhotoAt, history: this.history.status() };
  }
}
