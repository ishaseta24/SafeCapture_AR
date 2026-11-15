// Copied from RayBan/laptop/src/postprocess.js, hardened locally for finite geometry.
export const COCO_LABELS = [
  'person', 'bicycle', 'car', 'motorcycle', 'airplane', 'bus', 'train', 'truck', 'boat',
  'traffic light', 'fire hydrant', 'stop sign', 'parking meter', 'bench', 'bird', 'cat',
  'dog', 'horse', 'sheep', 'cow', 'elephant', 'bear', 'zebra', 'giraffe', 'backpack',
  'umbrella', 'handbag', 'tie', 'suitcase', 'frisbee', 'skis', 'snowboard', 'sports ball',
  'kite', 'baseball bat', 'baseball glove', 'skateboard', 'surfboard', 'tennis racket',
  'bottle', 'wine glass', 'cup', 'fork', 'knife', 'spoon', 'bowl', 'banana', 'apple',
  'sandwich', 'orange', 'broccoli', 'carrot', 'hot dog', 'pizza', 'donut', 'cake', 'chair',
  'couch', 'potted plant', 'bed', 'dining table', 'toilet', 'tv', 'laptop', 'mouse',
  'remote', 'keyboard', 'cell phone', 'microwave', 'oven', 'toaster', 'sink',
  'refrigerator', 'book', 'clock', 'vase', 'scissors', 'teddy bear', 'hair drier',
  'toothbrush',
];
export const DEFAULT_TARGET_LABELS = new Set([
  'person', 'chair', 'laptop', 'tv', 'cell phone', 'cup', 'bottle',
]);

export function intersectionOverUnion(a, b) {
  const intersection = Math.max(0, Math.min(a.x2, b.x2) - Math.max(a.x1, b.x1))
    * Math.max(0, Math.min(a.y2, b.y2) - Math.max(a.y1, b.y1));
  const areaA = Math.max(0, a.x2 - a.x1) * Math.max(0, a.y2 - a.y1);
  const areaB = Math.max(0, b.x2 - b.x1) * Math.max(0, b.y2 - b.y1);
  const union = areaA + areaB - intersection;
  return union > 0 ? intersection / union : 0;
}

export function validBox(box, width, height) {
  return box && ['x1', 'y1', 'x2', 'y2'].every((key) => Number.isFinite(box[key]))
    && box.x1 >= 0 && box.y1 >= 0 && box.x2 <= width && box.y2 <= height
    && box.x2 > box.x1 && box.y2 > box.y1;
}

export function postprocessYolo(output, dimensions, originalWidth, originalHeight, {
  confidenceThreshold = 0.25, iouThreshold = 0.45, maxDetections = 20,
  targetLabels = DEFAULT_TARGET_LABELS,
} = {}) {
  if (dimensions.length !== 3 || dimensions[0] !== 1) throw new Error('Unexpected YOLO shape');
  const channelFirst = dimensions[1] === 84;
  const classCount = channelFirst ? dimensions[1] - 4 : dimensions[2] - 4;
  const anchorCount = channelFirst ? dimensions[2] : dimensions[1];
  if (classCount !== 80 || output.length !== 84 * anchorCount) {
    throw new Error('Expected 80 COCO classes');
  }
  const at = (channel, anchor) => channelFirst
    ? output[channel * anchorCount + anchor] : output[anchor * 84 + channel];
  const candidates = [];
  for (let anchor = 0; anchor < anchorCount; anchor += 1) {
    let score = 0;
    let classId = 0;
    for (let channel = 4; channel < 84; channel += 1) {
      const value = at(channel, anchor);
      if (!Number.isFinite(value)) throw new Error('Nonfinite YOLO output');
      if (value > score) { score = value; classId = channel - 4; }
    }
    if (score < confidenceThreshold) continue;
    const label = COCO_LABELS[classId];
    if (targetLabels?.size && !targetLabels.has(label)) continue;
    const [cx, cy, w, h] = [0, 1, 2, 3].map((channel) => at(channel, anchor));
    if (![cx, cy, w, h].every(Number.isFinite)) throw new Error('Nonfinite YOLO geometry');
    const clampX = (x) => Math.max(0, Math.min(originalWidth, x * originalWidth / 640));
    const clampY = (y) => Math.max(0, Math.min(originalHeight, y * originalHeight / 640));
    const box = {
      x1: clampX(cx - w / 2), y1: clampY(cy - h / 2),
      x2: clampX(cx + w / 2), y2: clampY(cy + h / 2),
    };
    if (validBox(box, originalWidth, originalHeight)) {
      candidates.push({ label, class_id: classId, confidence: score, box });
    }
  }
  candidates.sort((a, b) => b.confidence - a.confidence);
  const kept = [];
  for (const candidate of candidates) {
    if (kept.length >= maxDetections) break;
    if (kept.some((prior) => prior.label === candidate.label
      && intersectionOverUnion(prior.box, candidate.box) > iouThreshold)) continue;
    kept.push(candidate);
  }
  return kept;
}

export class VisionObjectTracker {
  #tracks = [];
  #nextTrackId = 0;

  reset() { this.#tracks = []; }

  update(detections) {
    const pairs = [];
    this.#tracks.forEach((track, trackIndex) => {
      detections.forEach((detection, detectionIndex) => {
        if (track.label !== detection.label) return;
        const overlap = intersectionOverUnion(track.box, detection.box);
        if (overlap >= 0.2) pairs.push({ trackIndex, detectionIndex, overlap });
      });
    });
    pairs.sort((a, b) => b.overlap - a.overlap);
    const matchedTracks = new Set();
    const matchedDetections = new Set();
    for (const pair of pairs) {
      if (matchedTracks.has(pair.trackIndex) || matchedDetections.has(pair.detectionIndex)) continue;
      const track = this.#tracks[pair.trackIndex];
      const detection = detections[pair.detectionIndex];
      detection.track_id = track.id;
      track.box = detection.box;
      track.missedFrames = 0;
      matchedTracks.add(pair.trackIndex);
      matchedDetections.add(pair.detectionIndex);
    }
    this.#tracks.forEach((track, index) => {
      if (!matchedTracks.has(index)) track.missedFrames += 1;
    });
    this.#tracks = this.#tracks.filter((track) => track.missedFrames <= 10);
    detections.forEach((detection, index) => {
      if (matchedDetections.has(index)) return;
      const id = ++this.#nextTrackId;
      detection.track_id = id;
      this.#tracks.push({ id, label: detection.label, box: detection.box, missedFrames: 0 });
    });
    return detections;
  }
}
