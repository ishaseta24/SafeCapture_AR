import { RELAY_TOKEN } from './config.js';

export function requireToken(req, res, next) {
  const token = req.headers['x-relay-token'];
  if (!token || token !== RELAY_TOKEN) {
    return res.status(401).json({ error: 'Unauthorized' });
  }
  next();
}
