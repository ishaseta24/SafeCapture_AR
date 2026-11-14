import sharp from 'sharp';
import { MODEL_API_KEY, SAM_API_URL, MAX_IMAGE_WIDTH } from './config.js';

// One SAM request per prompt (SAM rule), run in parallel.
export async function detectObjects(imageJpegB64, prompts, originalWidth, originalHeight) {
  let processedB64 = imageJpegB64;
  let scaledWidth  = originalWidth;
  let scaledHeight = originalHeight;

  if (originalWidth > MAX_IMAGE_WIDTH) {
    const scale = MAX_IMAGE_WIDTH / originalWidth;
    scaledWidth  = MAX_IMAGE_WIDTH;
    scaledHeight = Math.round(originalHeight * scale);
    const resized = await sharp(Buffer.from(imageJpegB64, 'base64'))
      .resize(scaledWidth, scaledHeight)
      .jpeg({ quality: 85 })
      .toBuffer();
    processedB64 = resized.toString('base64');
  }

  const results = await Promise.all(prompts.map(p => callSam(processedB64, p)));

  const scaleX = originalWidth  / scaledWidth;
  const scaleY = originalHeight / scaledHeight;

  return results.flatMap((items, i) =>
    items.map(obj => ({
      label: obj.label ?? prompts[i],
      id:    obj.id   ?? i,
      box: {
        x1: Math.round((obj.box.x1 ?? 0) * scaleX),
        y1: Math.round((obj.box.y1 ?? 0) * scaleY),
        x2: Math.round((obj.box.x2 ?? 0) * scaleX),
        y2: Math.round((obj.box.y2 ?? 0) * scaleY),
      },
    }))
  );
}

async function callSam(imageB64, prompt) {
  // TODO(step 0): confirm exact request/response shape from raw test, then update here.
  const body = JSON.stringify({
    model: 'sam-3.1',
    messages: [{
      role: 'user',
      content: [
        { type: 'image_url', image_url: { url: `data:image/jpeg;base64,${imageB64}` } },
        { type: 'text', text: prompt },
      ],
    }],
  });

  const resp = await fetch(SAM_API_URL, {
    method: 'POST',
    headers: {
      'Content-Type':  'application/json',
      'Authorization': `Bearer ${MODEL_API_KEY}`,
    },
    body,
  });

  if (!resp.ok) {
    const text = await resp.text();
    throw new Error(`SAM ${resp.status}: ${text.slice(0, 300)}`);
  }

  return parseResponse(await resp.json(), prompt);
}

function parseResponse(data, prompt) {
  // Tolerant parser — update field names after step 0 raw test.
  const items = data.detections ?? data.output ?? data.results ?? data.objects ?? [];
  return items.map((item, i) => ({
    label: item.label ?? item.class ?? item.category ?? prompt,
    id:    item.id    ?? i,
    box:   item.box   ?? item.bbox ?? item.bounding_box ?? { x1: 0, y1: 0, x2: 0, y2: 0 },
  }));
}
