import sharp from 'sharp';

// Generates a pixel-precise foreground mask for a bounding box region.
// Returns { maskB64, maskW, maskH } or null if the box is too small.
//
// Algorithm:
//  1. Crop JPEG to the bounding box.
//  2. Convert to greyscale + compute Otsu threshold.
//  3. Determine polarity: is foreground lighter or darker than the box border?
//  4. Build 1-bit mask (1 = foreground / should be obscured).
//  5. Pack bits into bytes and return as base64.
export async function generateMask(imgBuf, box, imgW, imgH) {
  const left   = Math.max(0, Math.round(box.x1));
  const top    = Math.max(0, Math.round(box.y1));
  const width  = Math.min(imgW - left, Math.round(box.x2 - box.x1));
  const height = Math.min(imgH - top,  Math.round(box.y2 - box.y1));

  if (width < 4 || height < 4) return null;

  const { data, info } = await sharp(imgBuf)
    .extract({ left, top, width, height })
    .greyscale()
    .raw()
    .toBuffer({ resolveWithObject: true });

  const { width: w, height: h } = info;
  const threshold = otsuThreshold(data, w * h);

  // Sample border pixels (outer 15%) to estimate background brightness.
  // Sample centre (inner 40%) to estimate foreground brightness.
  const margin = Math.max(1, Math.round(Math.min(w, h) * 0.15));
  const cx0 = Math.round(w * 0.3), cx1 = Math.round(w * 0.7);
  const cy0 = Math.round(h * 0.3), cy1 = Math.round(h * 0.7);

  let borderSum = 0, borderN = 0, centerSum = 0, centerN = 0;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      const v = data[y * w + x];
      const isBorder = x < margin || x >= w - margin || y < margin || y >= h - margin;
      const isCenter = x > cx0 && x < cx1 && y > cy0 && y < cy1;
      if (isBorder) { borderSum += v; borderN++; }
      if (isCenter) { centerSum += v; centerN++; }
    }
  }
  const bgAvg = borderN ? borderSum / borderN : 128;
  const fgAvg = centerN ? centerSum / centerN : 128;
  // Foreground is the region that differs most from background.
  const fgBrighter = fgAvg > bgAvg;

  // Build 1-bit mask: 1 = foreground (pixelate this pixel).
  const totalPixels = w * h;
  const packed = new Uint8Array(Math.ceil(totalPixels / 8));
  for (let i = 0; i < totalPixels; i++) {
    const v = data[i];
    const isFg = fgBrighter ? v >= threshold : v <= threshold;
    if (isFg) packed[i >> 3] |= (1 << (7 - (i & 7)));
  }

  return {
    maskB64: Buffer.from(packed).toString('base64'),
    maskW: w,
    maskH: h,
  };
}

// Otsu's method: finds the threshold that maximises inter-class variance.
function otsuThreshold(data, length) {
  const hist = new Uint32Array(256);
  for (let i = 0; i < length; i++) hist[data[i]]++;

  let sum = 0;
  for (let i = 0; i < 256; i++) sum += i * hist[i];

  let sumB = 0, wB = 0, maxVar = 0, threshold = 128;
  for (let t = 0; t < 256; t++) {
    wB += hist[t];
    if (!wB || wB === length) continue;
    const wF = length - wB;
    sumB += t * hist[t];
    const mB = sumB / wB;
    const mF = (sum - sumB) / wF;
    const between = wB * wF * (mB - mF) ** 2;
    if (between > maxVar) { maxVar = between; threshold = t; }
  }
  return threshold;
}
