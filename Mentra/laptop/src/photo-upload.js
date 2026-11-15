import crypto from 'node:crypto';
import { Transform } from 'node:stream';
import Busboy from 'busboy';
import sharp from 'sharp';

export class HttpError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

export function bearerAuth(token) {
  const expected = Buffer.from(`Bearer ${token}`);
  return (request, response, next) => {
    const header = request.get('Authorization');
    const actual = Buffer.from(typeof header === 'string' ? header : '');
    if (!token || actual.length !== expected.length || !crypto.timingSafeEqual(actual, expected)) {
      response.status(401).json({ error: 'Invalid bearer token' });
      return;
    }
    next();
  };
}

// Shared by uploads and selection mutations. A lease spans parsing AND processing.
export class Admission {
  busy = false;
  acquire() {
    if (this.busy) throw new HttpError(503, 'Busy; retry after the current operation settles');
    this.busy = true;
    let released = false;
    return () => {
      if (!released) { this.busy = false; released = true; }
    };
  }
}

export function parsePhoto(request, { maxBytes, uploadTimeoutMs }) {
  return new Promise((resolve, reject) => {
    let parser;
    try {
      parser = Busboy({
        headers: request.headers,
        // Busboy emits partsLimit at the boundary ending the Nth part.
        // SDK 3.3 sends photo + requestId + type + success. Leave one
        // sentinel part so all four expected parts remain valid.
        limits: { fileSize: maxBytes + 1, files: 1, fields: 3, parts: 5, fieldSize: 129, fieldNameSize: 32 },
      });
    } catch {
      reject(new HttpError(400, 'Expected valid multipart form data'));
      return;
    }
    let settled = false;
    let photo;
    let requestId;
    const seenFields = new Set();
    let fileSeen = false;
    let total = 0;
    let fileBytes = 0;
    let chunks = [];
    const limiter = new Transform({
      transform(chunk, _encoding, callback) {
        total += chunk.length;
        // Cap the entire wire body, not just the file or per-part limits.
        callback(total > maxBytes + 16 * 1024
          ? new HttpError(413, 'Multipart body too large') : null, chunk);
      },
    });
    const cleanup = () => {
      clearTimeout(timer);
      request.off('aborted', onAbort);
      request.off('error', onRequestError);
      request.unpipe(limiter);
      limiter.unpipe(parser);
    };
    const finish = (error) => {
      if (settled) return;
      settled = true;
      cleanup();
      if (error) {
        chunks = [];
        photo = undefined;
        // A parser event can fire during its own write callback. Defer teardown
        // so it can finish updating its current file/field state safely.
        queueMicrotask(() => { parser.destroy(); limiter.destroy(); });
        request.resume(); // Discard, never buffer the rejected remainder.
        reject(error);
      } else {
        resolve({ photo, requestId });
      }
    };
    const onAbort = () => finish(new HttpError(400, 'Upload aborted'));
    const onRequestError = () => finish(new HttpError(400, 'Upload failed'));
    const timer = setTimeout(() => finish(new HttpError(400, 'Upload timed out')), uploadTimeoutMs);
    parser.on('file', (name, stream, info) => {
      stream.on('error', () => finish(new HttpError(400, 'Invalid file stream')));
      if (fileSeen || name !== 'photo') {
        stream.resume();
        finish(new HttpError(400, 'Expected exactly one photo'));
        return;
      }
      fileSeen = true;
      if (info.mimeType !== 'image/jpeg') {
        stream.resume();
        finish(new HttpError(415, 'Expected image/jpeg'));
        return;
      }
      stream.on('limit', () => finish(new HttpError(413, 'JPEG byte limit exceeded')));
      stream.on('data', (chunk) => {
        if (settled) return;
        fileBytes += chunk.length;
        chunks.push(chunk);
      });
      stream.on('end', () => {
        if (!settled) { photo = Buffer.concat(chunks, fileBytes); chunks = []; }
      });
    });
    parser.on('field', (name, value, info) => {
      if (settled) return;
      if (seenFields.has(name) || info.valueTruncated || info.nameTruncated) {
        finish(new HttpError(400, 'Duplicate or truncated multipart field'));
        return;
      }
      seenFields.add(name);
      if (name === 'type' && value === 'photo_upload') return;
      if (name === 'success' && value === 'true') return;
      if (name !== 'requestId') {
        finish(new HttpError(400, 'Unexpected multipart field or metadata value'));
        return;
      }
      if (!/^[\x21-\x7e]{1,128}$/.test(value)) {
        finish(new HttpError(400, 'Expected one nonblank ASCII requestId (1–128 bytes)'));
        return;
      }
      requestId = value;
    });
    for (const event of ['filesLimit', 'fieldsLimit', 'partsLimit']) {
      parser.on(event, () => finish(new HttpError(400, 'Duplicate or unexpected multipart fields')));
    }
    parser.on('error', () => finish(new HttpError(400, 'Malformed multipart data')));
    limiter.on('error', (error) => finish(error));
    parser.on('close', () => {
      if (!photo?.length || !requestId) finish(new HttpError(400, 'photo and requestId are required'));
      else finish();
    });
    request.once('aborted', onAbort);
    request.once('error', onRequestError);
    request.pipe(limiter).pipe(parser);
  });
}

export async function validateJpeg(jpeg, maxPixels) {
  if (jpeg[0] !== 0xff || jpeg[1] !== 0xd8) throw new HttpError(415, 'Expected JPEG bytes');
  try {
    // Metadata inspection does not decode pixels. Check dimensions before full decode.
    const metadata = await sharp(jpeg, { limitInputPixels: false, failOn: 'warning' }).metadata();
    const { width, height } = metadata;
    if (!width || !height || !Number.isSafeInteger(width * height)) {
      throw new HttpError(400, 'Invalid JPEG dimensions');
    }
    if (width * height > maxPixels) throw new HttpError(413, 'Decoded pixel limit exceeded');
    if (metadata.format !== 'jpeg' || (metadata.pages ?? 1) !== 1) {
      throw new HttpError(415, 'Expected single JPEG');
    }
    // metadata() alone accepts truncated JPEGs; force a strict complete decode.
    await sharp(jpeg, { limitInputPixels: maxPixels, failOn: 'warning' }).stats();
    return { jpeg, width, height };
  } catch (error) {
    if (error instanceof HttpError) throw error;
    throw new HttpError(400, 'Corrupt JPEG');
  }
}
