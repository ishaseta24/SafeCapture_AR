import sharp from 'sharp';
import { MODEL_API_KEY, SAM_API_URL, MAX_IMAGE_WIDTH } from './config.js';

// SAM box token grammar: <|box;x1=N;y1=N;x2=N;y2=N;w=W;h=H|>
// w/h are the image dimensions SAM used — needed to scale back to original pixels.
const BOX_RE = /(\d+)<\|box;x1=(\d+);y1=(\d+);x2=(\d+);y2=(\d+);w=(\d+);h=(\d+)\|>/g;

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

  return results.flatMap((items, promptIdx) =>
    items.map(obj => {
      const scaleX = originalWidth  / (obj._w || scaledWidth);
      const scaleY = originalHeight / (obj._h || scaledHeight);
      return {
        label: prompts[promptIdx],
        id:    obj.id,
        box: {
          x1: Math.round(obj.box.x1 * scaleX),
          y1: Math.round(obj.box.y1 * scaleY),
          x2: Math.round(obj.box.x2 * scaleX),
          y2: Math.round(obj.box.y2 * scaleY),
        },
      };
    })
  );
}

async function callSam(imageB64, prompt) {
  const body = JSON.stringify({
    model: 'sam-3.1',
    input: [{
      type: 'message',
      role: 'user',
      content: [
        { type: 'input_text',  text: prompt },
        { type: 'input_image', image_url: `data:image/jpeg;base64,${imageB64}` },
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

  const data = await resp.json();
  const text = data.output?.[0]?.content?.[0]?.text ?? '';
  // Phase 1 discovery: log raw SAM output to reveal any token types beyond boxes.
  console.log('[SAM raw]', text.slice(0, 500));
  return parseBoxes(text);
}

function parseBoxes(text) {
  const boxes = [];
  BOX_RE.lastIndex = 0;
  let m;
  while ((m = BOX_RE.exec(text)) !== null) {
    boxes.push({
      id:  parseInt(m[1], 10),
      box: { x1: parseInt(m[2], 10), y1: parseInt(m[3], 10), x2: parseInt(m[4], 10), y2: parseInt(m[5], 10) },
      _w:  parseInt(m[6], 10),
      _h:  parseInt(m[7], 10),
    });
  }
  return boxes;
}
