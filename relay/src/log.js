import fs from 'fs';
import path from 'path';

const LOGS_DIR = new URL('../../logs', import.meta.url).pathname;
fs.mkdirSync(LOGS_DIR, { recursive: true });
const stream = fs.createWriteStream(path.join(LOGS_DIR, 'relay.jsonl'), { flags: 'a' });

export function logCall({ frameId, latencyMs, objectCount, promptCount, error }) {
  const entry = JSON.stringify({
    ts:           new Date().toISOString(),
    frame_id:     frameId,
    latency_ms:   latencyMs,
    object_count: objectCount,
    prompt_count: promptCount,
    error:        error ?? null,
  });
  stream.write(entry + '\n');
  // Mask key in stdout but log everything else
  console.log(`[relay] ${entry}`);
}
