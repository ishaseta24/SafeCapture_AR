import sharp from 'sharp';
import fs from 'fs/promises';
import path from 'path';
import { DEBUG_FRAMES } from './config.js';

const DEBUG_DIR = new URL('../../debug', import.meta.url).pathname;

export async function saveDebugFrame(frameId, imageJpegB64, objects, width, height) {
  if (!DEBUG_FRAMES || !objects.length) return;
  await fs.mkdir(DEBUG_DIR, { recursive: true });

  const svgRects = objects.map(({ label, box: { x1, y1, x2, y2 } }) => `
    <rect x="${x1}" y="${y1}" width="${x2 - x1}" height="${y2 - y1}"
          fill="none" stroke="#00FF00" stroke-width="3"/>
    <text x="${x1 + 4}" y="${y1 + 18}" font-size="16" fill="#00FF00" font-family="monospace">${label}</text>
  `).join('');

  const svg = Buffer.from(
    `<svg width="${width}" height="${height}" xmlns="http://www.w3.org/2000/svg">${svgRects}</svg>`
  );

  await sharp(Buffer.from(imageJpegB64, 'base64'))
    .composite([{ input: svg, gravity: 'northwest' }])
    .jpeg({ quality: 90 })
    .toFile(path.join(DEBUG_DIR, `frame_${frameId}.jpg`));
}
