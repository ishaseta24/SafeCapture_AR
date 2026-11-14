import { MAX_PROMPTS, DAILY_CALL_CAP } from './config.js';

let inFlight    = false;
let dailyCount  = 0;
let lastResetDay = new Date().toDateString();

export function acquireSlot() {
  const today = new Date().toDateString();
  if (today !== lastResetDay) { dailyCount = 0; lastResetDay = today; }
  if (inFlight)              return 'busy';
  if (dailyCount >= DAILY_CALL_CAP) return 'cap';
  inFlight = true;
  dailyCount++;
  return 'ok';
}

export function releaseSlot() { inFlight = false; }

export { MAX_PROMPTS };
