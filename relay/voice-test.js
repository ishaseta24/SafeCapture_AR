// Direct Meta ASR smoke test — bypasses the relay entirely.
// Usage:  node voice-test.js
// Generates a 1-second 440 Hz tone WAV and calls Meta's ASR API directly.
import 'dotenv/config';
import { randomUUID } from 'node:crypto';

const MODEL_API_KEY = process.env.MODEL_API_KEY;
if (!MODEL_API_KEY) { console.error('MODEL_API_KEY not set in .env'); process.exit(1); }

// Build a 1-second mono 16-bit PCM WAV at 24 000 Hz (440 Hz sine tone)
function makeToneWav(durationSec = 1.0, hz = 440, sampleRate = 24000) {
  const samples   = Math.round(sampleRate * durationSec);
  const dataBytes = samples * 2;
  const buf       = Buffer.alloc(44 + dataBytes);
  buf.write('RIFF', 0);                   buf.writeUInt32LE(36 + dataBytes, 4);
  buf.write('WAVE', 8);                   buf.write('fmt ', 12);
  buf.writeUInt32LE(16, 16);              buf.writeUInt16LE(1, 20);  // PCM
  buf.writeUInt16LE(1, 22);              buf.writeUInt32LE(sampleRate, 24);
  buf.writeUInt32LE(sampleRate * 2, 28); buf.writeUInt16LE(2, 32);
  buf.writeUInt16LE(16, 34);             buf.write('data', 36);
  buf.writeUInt32LE(dataBytes, 40);
  for (let i = 0; i < samples; i++) {
    const v = Math.round(Math.sin(2 * Math.PI * hz * i / sampleRate) * 16000);
    buf.writeInt16LE(v, 44 + i * 2);
  }
  return buf;
}

const wav      = makeToneWav();
const boundary = `----VoiceTest${randomUUID().replaceAll('-', '')}`;
const settings = JSON.stringify({ model: 'muse-voice-transcribe-1.0', mode: 'PUSH_TO_TALK', audioEncoding: 'WAV' });

const body = Buffer.concat([
  Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="request"\r\nContent-Type: application/json\r\n\r\n${settings}\r\n`),
  Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="audio"; filename="test.wav"\r\nContent-Type: audio/wav\r\n\r\n`),
  wav,
  Buffer.from(`\r\n--${boundary}--\r\n`),
]);

console.log(`Calling Meta ASR directly — ${body.length} byte multipart body …`);

const res = await fetch('https://api.meta.ai/v1/asr/transcribe', {
  method:  'POST',
  headers: {
    Authorization:  `Bearer ${MODEL_API_KEY}`,
    Accept:         'application/json',
    'Content-Type': `multipart/form-data; boundary=${boundary}`,
  },
  body,
  signal: AbortSignal.timeout(45_000),
});

console.log('HTTP', res.status);
console.log(await res.text());
