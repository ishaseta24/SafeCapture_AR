// Independent adaptation of RayBan/laptop/src/privacy.js.
import sharp from 'sharp';
import { validBox } from './postprocess.js';

const clamp = (n, min, max) => Math.min(max, Math.max(min, n));
const escapeXml = (value) => String(value).replace(/[<>&"']/g, (character) => ({
  '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;',
}[character]));

function pixelateBox(pixels, width, height, box, blockSize = 16, padding = 8) {
  if (!validBox(box, width, height)) throw new Error('Unsafe mask geometry');
  const left = clamp(Math.floor(box.x1 - padding), 0, width - 1);
  const right = clamp(Math.ceil(box.x2 + padding), 0, width - 1);
  const top = clamp(Math.floor(box.y1 - padding), 0, height - 1);
  const bottom = clamp(Math.ceil(box.y2 + padding), 0, height - 1);
  for (let blockY = top; blockY <= bottom; blockY += blockSize) {
    const endY = Math.min(blockY + blockSize, bottom + 1);
    for (let blockX = left; blockX <= right; blockX += blockSize) {
      const endX = Math.min(blockX + blockSize, right + 1);
      const sum = [0, 0, 0];
      const count = (endX - blockX) * (endY - blockY);
      for (let y = blockY; y < endY; y += 1) {
        for (let x = blockX; x < endX; x += 1) {
          const offset = (y * width + x) * 3;
          for (let c = 0; c < 3; c += 1) sum[c] += pixels[offset + c];
        }
      }
      for (let y = blockY; y < endY; y += 1) {
        for (let x = blockX; x < endX; x += 1) {
          const offset = (y * width + x) * 3;
          for (let c = 0; c < 3; c += 1) pixels[offset + c] = Math.round(sum[c] / count);
        }
      }
    }
  }
}

export async function renderPrivacyFrame(jpeg, detections, selected, forceFullMask, maxPixels) {
  const { data, info } = await sharp(jpeg, { limitInputPixels: maxPixels, failOn: 'warning' })
    .toColourspace('srgb').removeAlpha().raw().toBuffer({ resolveWithObject: true });
  if (info.channels !== 3) throw new Error('Unexpected pixel layout');
  const pixels = Buffer.from(data);
  if (forceFullMask) {
    // Opaque black is deliberately stronger than full-frame pixelation.
    pixels.fill(0);
  } else {
    for (const item of selected.values()) {
      if (item.sourceWidth !== info.width || item.sourceHeight !== info.height) {
        throw new Error('Mask dimensions changed');
      }
      pixelateBox(pixels, info.width, info.height, item.box);
    }
  }
  const shapes = forceFullMask ? '' : detections.map((detection) => {
    if (!validBox(detection.box, info.width, info.height)) throw new Error('Unsafe overlay');
    const { x1, y1, x2, y2 } = detection.box;
    const color = selected.has(detection.track_id) ? '#00ffff' : '#ffe34d';
    const label = escapeXml(`${detection.label} ${Math.round(detection.confidence * 100)}%`);
    return `<rect x="${x1}" y="${y1}" width="${x2 - x1}" height="${y2 - y1}" fill="none" stroke="${color}" stroke-width="3"/><text x="${x1}" y="${Math.max(18, y1 - 4)}" fill="white" font-size="16" font-family="monospace" stroke="black" stroke-width="3" paint-order="stroke">${label}</text>`;
  }).join('');
  const image = sharp(pixels, { raw: { width: info.width, height: info.height, channels: 3 } });
  if (shapes) {
    image.composite([{ input: Buffer.from(
      `<svg width="${info.width}" height="${info.height}" xmlns="http://www.w3.org/2000/svg">${shapes}</svg>`,
    ) }]);
  }
  return image.jpeg({ quality: 75 }).toBuffer();
}
