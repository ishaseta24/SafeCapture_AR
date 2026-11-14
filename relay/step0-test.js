import fs from 'fs';
import 'dotenv/config';

const IMAGE_PATH = process.argv[2] || '../images.jpeg';
const PROMPT     = process.argv[3] || 'bird';

const b64 = fs.readFileSync(IMAGE_PATH).toString('base64');

console.log(`Sending ${(b64.length / 1024).toFixed(1)} KB base64 | prompt: "${PROMPT}"`);

const resp = await fetch('https://api.meta.ai/v1/responses', {
  method: 'POST',
  headers: {
    'Content-Type':  'application/json',
    'Authorization': `Bearer ${process.env.MODEL_API_KEY}`,
  },
  body: JSON.stringify({
    model: 'sam-3.1',
    input: [{
      type: 'message',
      role: 'user',
      content: [
        { type: 'input_text',  text: PROMPT },
        { type: 'input_image', image_url: `data:image/jpeg;base64,${b64}` },
      ],
    }],
  }),
});

const data = await resp.json();
console.log('Status:', resp.status);
console.log('output_text:', data.output?.[0]?.content?.[0]?.text ?? '(empty)');
console.log('Full response:', JSON.stringify(data, null, 2));
