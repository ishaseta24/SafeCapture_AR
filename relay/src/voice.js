import { randomUUID } from 'node:crypto';

const META_TRANSCRIBE_URL = 'https://api.meta.ai/v1/asr/transcribe';
const MODEL = 'muse-voice-transcribe-1.0';
const MAX_AUDIO_BYTES = 1024 * 1024;
const MAX_AUDIO_SECONDS = 15;

export class VoiceTranscriptionError extends Error {
  constructor(statusCode, publicMessage) {
    super(publicMessage);
    this.statusCode = statusCode;
    this.publicMessage = publicMessage;
  }
}

export function inspectPcmWav(audio) {
  if (!Buffer.isBuffer(audio) || audio.length < 44 || audio.length > MAX_AUDIO_BYTES)
    throw new VoiceTranscriptionError(400, 'Invalid or oversized WAV recording.');
  if (audio.toString('ascii', 0, 4) !== 'RIFF'
      || audio.toString('ascii', 8, 12) !== 'WAVE'
      || audio.readUInt32LE(4) + 8 !== audio.length)
    throw new VoiceTranscriptionError(400, 'Invalid WAV recording.');

  let format = null;
  let dataLength = null;
  let offset = 12;
  while (offset + 8 <= audio.length) {
    const chunkName = audio.toString('ascii', offset, offset + 4);
    const chunkLength = audio.readUInt32LE(offset + 4);
    const chunkStart = offset + 8;
    const chunkEnd = chunkStart + chunkLength;
    if (chunkEnd > audio.length)
      throw new VoiceTranscriptionError(400, 'Invalid WAV recording.');

    if (chunkName === 'fmt ' && chunkLength >= 16) {
      format = {
        encoding: audio.readUInt16LE(chunkStart),
        channels: audio.readUInt16LE(chunkStart + 2),
        sampleRate: audio.readUInt32LE(chunkStart + 4),
        byteRate: audio.readUInt32LE(chunkStart + 8),
        blockAlign: audio.readUInt16LE(chunkStart + 12),
        bitsPerSample: audio.readUInt16LE(chunkStart + 14),
      };
    } else if (chunkName === 'data') {
      dataLength = chunkLength;
      break;
    }
    offset = chunkEnd + (chunkLength & 1);
  }

  if (!format || dataLength === null || format.encoding !== 1 || format.channels !== 1
      || ![16000, 24000].includes(format.sampleRate) || format.bitsPerSample !== 16
      || format.blockAlign !== 2 || format.byteRate !== format.sampleRate * 2
      || dataLength % 2 !== 0)
    throw new VoiceTranscriptionError(400, 'Recording must be mono 16-bit PCM WAV at 16 or 24 kHz.');

  const durationSeconds = dataLength / format.byteRate;
  if (durationSeconds < 0.2 || durationSeconds > MAX_AUDIO_SECONDS)
    throw new VoiceTranscriptionError(400, 'Recording duration must be between 0.2 and 15 seconds.');

  return { sampleRate: format.sampleRate, durationSeconds };
}

export async function transcribeVoice(audio, fetchImpl = fetch, modelApiKey = process.env.MODEL_API_KEY) {
  if (!modelApiKey)
    throw new VoiceTranscriptionError(503, 'Transcription service is not configured.');
  inspectPcmWav(audio);

  const boundary = `----SafeCaptureVoice${randomUUID().replaceAll('-', '')}`;
  const settings = JSON.stringify({
    model: MODEL,
    mode: 'PUSH_TO_TALK',
    audioEncoding: 'WAV',
  });
  const multipartBody = Buffer.concat([
    Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="request"\r\nContent-Type: application/json\r\n\r\n${settings}\r\n`, 'utf8'),
    Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="audio"; filename="command.wav"\r\nContent-Type: audio/wav\r\n\r\n`, 'utf8'),
    audio,
    Buffer.from(`\r\n--${boundary}--\r\n`, 'ascii'),
  ]);

  let response;
  try {
    response = await fetchImpl(META_TRANSCRIBE_URL, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${modelApiKey}`,
        Accept: 'application/json',
        'Content-Type': `multipart/form-data; boundary=${boundary}`,
      },
      body: multipartBody,
      signal: AbortSignal.timeout(30000),
    });
  } catch {
    throw new VoiceTranscriptionError(502, 'Transcription service is unavailable.');
  } finally {
    multipartBody.fill(0);
  }

  if (response.status === 429)
    throw new VoiceTranscriptionError(429, 'Transcription service is rate limited. Please retry.');
  if (!response.ok)
    throw new VoiceTranscriptionError(502, 'Transcription service rejected the recording.');

  let result;
  try {
    result = await response.json();
  } catch {
    throw new VoiceTranscriptionError(502, 'Transcription service returned an invalid response.');
  }

  if (typeof result?.transcript !== 'string' || !result.transcript.trim())
    throw new VoiceTranscriptionError(422, 'No final transcript was returned.');

  // The non-streaming PUSH_TO_TALK endpoint returns a completed transcript.
  // Never expose upstream response bodies, which may contain sensitive content.
  return { transcript: result.transcript, final: true };
}
