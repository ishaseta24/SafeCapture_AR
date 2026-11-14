import './config.js';
import express from 'express';
import { requireToken }            from './auth.js';
import { acquireSlot, releaseSlot, MAX_PROMPTS } from './limits.js';
import { detectObjects }           from './sam.js';
import { saveDebugFrame }          from './annotate.js';
import { logCall }                 from './log.js';
import { PORT }                    from './config.js';

const app = express();
app.use(express.json({ limit: '10mb' }));

app.get('/health', (_req, res) => res.json({ ok: true }));

app.post('/detect', requireToken, async (req, res) => {
  const { frame_id, image_jpeg_b64, prompts, width = 1280, height = 720 } = req.body;

  if (!image_jpeg_b64 || !Array.isArray(prompts) || prompts.length === 0)
    return res.status(400).json({ error: 'Missing image_jpeg_b64 or prompts' });

  const cappedPrompts = prompts.slice(0, MAX_PROMPTS);
  const slot = acquireSlot();
  if (slot === 'busy') return res.status(429).json({ error: 'Request in flight' });
  if (slot === 'cap')  return res.status(429).json({ error: 'Daily cap reached' });

  const t0 = Date.now();
  let objects = [];
  let error   = null;

  try {
    objects = await detectObjects(image_jpeg_b64, cappedPrompts, width, height);
  } catch (err) {
    error = err.message;
    console.error('[server] SAM error:', err.message);
  } finally {
    releaseSlot();
  }

  const latencyMs = Date.now() - t0;
  logCall({ frameId: frame_id, latencyMs, objectCount: objects.length, promptCount: cappedPrompts.length, error });

  if (!error)
    saveDebugFrame(frame_id, image_jpeg_b64, objects, width, height).catch(() => {});

  if (error) return res.status(502).json({ error });

  res.json({ frame_id, width, height, latency_ms: latencyMs, objects });
});

app.listen(PORT, () => console.log(`[server] relay listening on :${PORT}`));
