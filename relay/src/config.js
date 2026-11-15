import 'dotenv/config';

const MODEL_API_KEY  = process.env.MODEL_API_KEY;
const RELAY_TOKEN    = process.env.RELAY_TOKEN;
const PORT           = parseInt(process.env.PORT          || '3000', 10);
const MAX_PROMPTS    = parseInt(process.env.MAX_PROMPTS   || '3',    10);
const DAILY_CALL_CAP = parseInt(process.env.DAILY_CALL_CAP || '1000', 10);
const SAM_API_URL    = process.env.SAM_API_URL   || 'https://api.meta.ai/v1/responses';
const MAX_IMAGE_WIDTH = parseInt(process.env.MAX_IMAGE_WIDTH || '960', 10);
const DEBUG_FRAMES   = process.env.DEBUG_FRAMES === 'true';

if (!MODEL_API_KEY) { console.error('[config] MODEL_API_KEY missing — set it in .env'); process.exit(1); }
if (!RELAY_TOKEN)   { console.error('[config] RELAY_TOKEN missing — set it in .env');   process.exit(1); }

export { MODEL_API_KEY, RELAY_TOKEN, PORT, MAX_PROMPTS, DAILY_CALL_CAP, SAM_API_URL, MAX_IMAGE_WIDTH, DEBUG_FRAMES };
