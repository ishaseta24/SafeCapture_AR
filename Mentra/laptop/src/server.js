// Independent adaptation of RayBan/laptop/src/server.js; snapshots, not a frame queue.
import http from 'node:http';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import dotenv from 'dotenv';
import express from 'express';
import { Admission, HttpError, bearerAuth, parsePhoto, validateJpeg } from './photo-upload.js';
import { PhotoPipeline } from './pipeline.js';
import { Viewers } from './viewers.js';

export function readConfig(env = process.env) {
  const integer = (key, fallback, min, max) => {
    const value = env[key] === undefined ? fallback : Number(env[key]);
    if (!Number.isInteger(value) || value < min || value > max) throw new Error(`Invalid ${key}`);
    return value;
  };
  const token = env.SAFE_CAPTURE_INGEST_TOKEN;
  if (!token || token === 'replace-with-a-random-local-token'
    || !/^[\x21-\x7e]{32,256}$/.test(token)) {
    throw new Error('Set a random SAFE_CAPTURE_INGEST_TOKEN (32–256 ASCII characters)');
  }
  const host = env.HOST ?? '0.0.0.0';
  if (!['0.0.0.0', '127.0.0.1'].includes(host)) {
    throw new Error('HOST must be 0.0.0.0 (trusted LAN) or 127.0.0.1 (USB/local only)');
  }
  const autoMaskValue = env.SAFE_CAPTURE_AUTO_MASK_CLASSES ?? '';
  if (typeof autoMaskValue !== 'string') throw new Error('Invalid SAFE_CAPTURE_AUTO_MASK_CLASSES');
  const autoMaskClasses = autoMaskValue.trim() ? autoMaskValue.split(',').map((label) => label.trim()) : [];
  if (autoMaskClasses.some((label) => !['tv', 'laptop'].includes(label))) {
    throw new Error('SAFE_CAPTURE_AUTO_MASK_CLASSES must be empty or a comma-separated list of tv,laptop');
  }
  return {
    token, host, autoMaskClasses: [...new Set(autoMaskClasses)], port: integer('PORT', 8091, 1024, 65535),
    maxBytes: integer('MAX_PHOTO_BYTES', 5242880, 1024, 10485760),
    maxPixels: integer('MAX_PHOTO_PIXELS', 12000000, 1, 20000000),
    uploadTimeoutMs: integer('UPLOAD_TIMEOUT_MS', 20000, 100, 60000),
    staleMs: integer('STALE_MS', 15000, 100, 60000),
    maxViewers: integer('MAX_VIEWERS', 8, 1, 16),
  };
}

export function createApp({ detector, config, renderer, now, parse = parsePhoto }) {
  const app = express();
  app.disable('x-powered-by');
  const admission = new Admission();
  const viewers = new Viewers({ maxViewers: config.maxViewers, maxFrameBytes: config.maxBytes });
  const pipeline = new PhotoPipeline({
    detector, renderer, now, autoMaskClasses: config.autoMaskClasses,
    staleMs: config.staleMs, maxPixels: config.maxPixels,
    maxOutputBytes: config.maxBytes, onPublish: (jpeg) => viewers.publish(jpeg),
    onWithhold: () => viewers.close(),
  });
  const auth = bearerAuth(config.token);
  app.use((_request, response, next) => {
    response.set('Cache-Control', 'no-store');
    response.set('X-Content-Type-Options', 'nosniff');
    response.set('Referrer-Policy', 'no-referrer');
    next();
  });
  const acquire = (_request, response, next) => {
    try {
      const release = admission.acquire();
      response.locals.release = release;
      next();
    } catch (error) { next(error); }
  };
  const operation = (handler) => async (request, response, next) => {
    try { await handler(request, response); }
    catch (error) { next(error); }
    finally { response.locals.release?.(); }
  };
  app.post('/mentra/photo', auth, acquire, operation(async (request, response) => {
    const controller = new AbortController();
    const onClose = () => { if (!response.writableFinished) controller.abort(); };
    response.once('close', onClose);
    try {
      const { photo, requestId } = await parse(request, config);
      const frame = await validateJpeg(photo, config.maxPixels);
      if (controller.signal.aborted) throw new HttpError(400, 'Upload aborted');
      const metadata = await pipeline.process(frame, requestId, controller.signal);
      response.json(metadata); // Only after inference + safe rendering.
    } finally {
      response.off('close', onClose);
    }
  }));
  app.get('/health', (_request, response) => response.json({ ok: true, modelLoaded: !!detector }));
  app.get('/status', (_request, response) => response.json({ ok: true, busy: admission.busy, ...pipeline.status() }));
  // A complete, processed JPEG avoids single-frame MJPEG browser buffering.
  // Never serve pipeline.frame: it contains the unprocessed camera image.
  app.get('/snapshot.jpg', (request, response) => {
    response.set({ 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
    if (!pipeline.frameId || request.query.frameId !== pipeline.frameId) {
      return response.status(409).json({ error: 'Photo changed or frame identity missing; refresh the photo' });
    }
    if (!pipeline.output) return response.status(204).end();
    response.type('image/jpeg').send(pipeline.output);
  });
  // Exact in-memory source + published revision only: no paths, raw, or latest fallback.
  app.get('/history/:frameId/photo.jpg', (request, response, next) => {
    try {
      const jpeg = pipeline.history.image(request.params.frameId, request.query.revision);
      response.type('image/jpeg').send(jpeg);
    } catch (error) { next(error); }
  });
  app.get('/history/:frameId/thumbnail.jpg', (request, response, next) => {
    try {
      const jpeg = pipeline.history.image(request.params.frameId, request.query.revision, true);
      response.type('image/jpeg').send(jpeg);
    } catch (error) { next(error); }
  });
  app.get('/stream', (_request, response) => {
    if (!viewers.add(response, pipeline.output)) response.status(503).json({ error: 'Viewer limit reached' });
  });
  // Acquire before JSON parsing too; release parsing failures through the error handler.
  app.post('/selection', auth, acquire, express.json({ limit: '1kb', strict: true }), operation(async (request, response) => {
    await pipeline.select(request.body?.x, request.body?.y, request.body?.frameId);
    response.json(pipeline.selectionStatus());
  }));
  app.post('/selection/clear', auth, acquire, express.json({ limit: '1kb', strict: true }), operation(async (request, response) => {
    if (!pipeline.frameId || request.body?.frameId !== pipeline.frameId) {
      throw new HttpError(409, 'Photo changed or frame identity missing; refresh the photo before discarding');
    }
    pipeline.clear();
    viewers.close(); // Existing browser display is reset by the viewer on clear.
    response.json({ cleared: true, ...pipeline.selectionStatus() });
  }));
  app.get('/', (_request, response) => response.type('html').send(VIEWER_HTML));
  app.use((error, _request, response, _next) => {
    response.locals.release?.();
    if (response.destroyed || response.headersSent) return;
    const status = error instanceof HttpError ? error.status : error.type === 'entity.too.large' ? 413 : 400;
    if (status === 503) response.set('Retry-After', '2');
    response.status(status).json({ error: error instanceof HttpError ? error.message : 'Invalid request' });
  });
  return { app, pipeline, admission, viewers, close: () => { viewers.close(); pipeline.close(); } };
}

const VIEWER_HTML = `<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>SafeCapture · Mentra snapshots</title>
<style>body{max-width:960px;margin:24px auto;padding:0 16px;background:#111;color:#eee;font:16px system-ui}
#stream{width:100%;min-height:200px;background:#000;cursor:default}#stream.editable{cursor:crosshair}
button{padding:10px;margin:6px}button:disabled{opacity:.5;cursor:not-allowed}#status{white-space:pre-wrap}
#gallery{display:grid;grid-template-columns:repeat(auto-fill,minmax(145px,1fr));gap:10px}
#gallery button{margin:0;padding:7px;background:#222;color:#eee;border:2px solid #555;text-align:left;cursor:pointer}
#gallery button[aria-pressed="true"]{border-color:#00ffff}
#gallery img{display:block;width:100%;height:105px;min-height:0;object-fit:contain;background:#000;cursor:pointer}
#gallery span{display:block;font-size:13px;margin-top:5px}.muted{color:#bbb}</style>
<h1>Mentra snapshots</h1>
<p>Local prototype: trusted Wi-Fi LAN or USB/ADB reverse. Masking is not universal privacy.
Raw photos reach this Mac before masking. No control token is needed to view processed photos.</p>
<p id="policy-status" role="status">Reading automatic-mask policy…</p>
<p>Automatic policy masks only detected, configured TV/laptop regions. A laptop mask covers the
entire laptop detection, not just its display. Undetected screens, text and documents may remain visible.</p>
<p id="preview-status" role="status">Waiting for a processed photo…</p>
<p id="selected-photo" role="status">Latest photo · waiting</p>
<img id="stream" hidden alt="Latest processed snapshot with automatic and manual masks">
<button id="latest">Show latest</button>
<p>Automatic-policy masks stay on and cannot be toggled. Click other detected boxes to toggle
manual pixelation with a control token while following the latest photo. Cyan boxes are masked.
Saved-photo browsing is read-only; Show latest restores current-photo controls.
Lost/stale manual tracks across new photos latch a full-frame mask.
Clear discards the current photo and manual selections; automatic policy stays active for the next photo.</p>
<button id="token">Enter control token</button><button id="clear" disabled>Clear manual selections &amp; discard photo</button>
<p id="action-status" role="status"></p>
<p id="status" role="status">Waiting for glasses photos…</p>
<h2>Recent processed photos <span id="gallery-count">0 / 20</span></h2>
<p class="muted">History begins with captures after this receiver starts. Up to 20 photos / 64 MiB,
including small thumbnails, in memory only. Oldest photos are evicted; restart clears everything.
No raw history or image files are saved. Selecting a thumbnail does not take a photo.</p>
<p id="gallery-empty">No saved processed photos yet.</p>
<div id="gallery" aria-label="Recent processed photos"></div>
<script>
 let token='',revision=null,displayedFrameId=null,displayedObjects=[],displayedAutoIds=[],
 updating=false,mutating=false,stateGeneration=0,followLatest=true,selectedFrameId=null,
 latestStatus=null,galleryKey='',blockedFrameId=null;
 const image=document.querySelector('#stream'),status=document.querySelector('#status'),
 previewStatus=document.querySelector('#preview-status'),actionStatus=document.querySelector('#action-status'),
 policyStatus=document.querySelector('#policy-status'),selectedLabel=document.querySelector('#selected-photo'),
 gallery=document.querySelector('#gallery'),clearButton=document.querySelector('#clear'),
 tokenButton=document.querySelector('#token'),latestButton=document.querySelector('#latest');
 function canEdit(){
 return followLatest&&!mutating&&!latestStatus?.busy&&!image.hidden&&!!displayedFrameId&&
  displayedFrameId===latestStatus?.frame_id&&latestStatus.output_available;
 }
 function controls(){
 clearButton.disabled=!canEdit();tokenButton.disabled=mutating||!followLatest;
 latestButton.disabled=mutating;image.classList.toggle('editable',!!canEdit());
 }
 function hideSnapshot(message='Waiting for a processed photo…'){
 image.onload=null;image.onerror=null;image.hidden=true;image.removeAttribute('src');revision=null;displayedFrameId=null;
 displayedObjects=[];displayedAutoIds=[];
 previewStatus.textContent=message;previewStatus.hidden=false;controls();
 }
 function photoUrl(photo,thumbnail=false){
 return '/history/'+encodeURIComponent(photo.frame_id)+'/'+(thumbnail?'thumbnail.jpg':'photo.jpg')+
  '?revision='+encodeURIComponent(photo.revision);
 }
 function captured(photo){return new Date(photo.captured_at).toLocaleString()}
 function renderGallery(s){
 const photos=s.history.photos.filter(p=>p.frame_id!==blockedFrameId);
 const key=JSON.stringify(photos);
 document.querySelector('#gallery-count').textContent=photos.length+' / '+s.history.limit;
 document.querySelector('#gallery-empty').hidden=photos.length>0;
 if(key!==galleryKey){
  galleryKey=key;
  const fragment=document.createDocumentFragment();
  for(const photo of photos){
   const button=document.createElement('button'),caption=document.createElement('span');
   button.type='button';button.dataset.frameId=photo.frame_id;
   button.setAttribute('aria-label','View processed photo captured '+captured(photo));
   if(photo.thumbnail_available){
    const thumb=document.createElement('img');thumb.alt='';thumb.width=240;thumb.height=160;
    thumb.onload=()=>{thumb.onload=null};
    thumb.onerror=()=>{
     const missing=document.createElement('span');missing.textContent='Thumbnail unavailable';
     thumb.replaceWith(missing);
    };
    thumb.src=photoUrl(photo,true);button.append(thumb);
   }else{
    const missing=document.createElement('span');missing.textContent='Thumbnail unavailable · open full photo';
    button.append(missing);
   }
   caption.textContent=captured(photo)+' · '+photo.selected_count+' masked'+(photo.fail_closed?' · full-frame mask':'');
   button.append(caption);
   button.onclick=()=>{
    if(mutating)return;
    followLatest=false;selectedFrameId=photo.frame_id;stateGeneration++;
    hideSnapshot('Loading saved processed photo…');
    if(latestStatus){renderGallery(latestStatus);renderSelection(latestStatus)}
   };
   fragment.append(button);
  }
  gallery.replaceChildren(fragment);
 }
 const active=followLatest?s.frame_id:selectedFrameId;
 for(const button of gallery.children){
  button.setAttribute('aria-pressed',String(button.dataset.frameId===active));
  button.disabled=mutating;
 }
 }
 function renderSelection(s){
 const id=followLatest?s.frame_id:selectedFrameId;
 const photo=s.history.photos.find(p=>p.frame_id===id&&p.frame_id!==blockedFrameId);
 selectedLabel.textContent=(followLatest?'Latest photo':'Saved photo · read-only')+
  (photo?' · '+captured(photo)+' · revision '+photo.revision:' · unavailable');
 if(!photo){
  const message=followLatest?'No processed latest photo available. Take a photo, or check processing status.':
   (id===s.frame_id&&!s.output_available?'Selected photo is being processed or withheld; its previous version is hidden.':
    'Selected photo is no longer available (evicted, discarded, or receiver restarted). Choose another or Show latest.');
  hideSnapshot(message);return;
 }
 if(followLatest&&!s.output_available){hideSnapshot('Latest photo is being processed or withheld.');return}
 const nextRevision=photo.frame_id+':'+photo.revision;
 if(revision!==nextRevision||!image.getAttribute('src')){
  const generation=stateGeneration;
  revision=nextRevision;displayedFrameId=null;displayedObjects=[];displayedAutoIds=[];
  image.hidden=true;previewStatus.hidden=false;previewStatus.textContent='Loading processed photo…';
  image.alt=(followLatest?'Latest':'Saved')+' processed photo captured '+captured(photo);
  image.onload=()=>{
   if(generation!==stateGeneration||revision!==nextRevision||mutating)return;
   displayedFrameId=photo.frame_id;
   displayedObjects=followLatest?s.objects:[];displayedAutoIds=followLatest?s.auto_selected_track_ids:[];
   image.hidden=false;previewStatus.hidden=true;controls();
  };
  image.onerror=()=>{
   if(generation===stateGeneration&&revision===nextRevision){
    hideSnapshot('This photo version is unavailable or changed. Refreshing gallery metadata…');
   }
  };
  image.src=photoUrl(photo);
 }
 controls();
 }
 latestButton.onclick=()=>{
 if(mutating)return;
 followLatest=true;selectedFrameId=null;stateGeneration++;
 hideSnapshot('Following latest processed photo…');
 if(latestStatus){renderGallery(latestStatus);renderSelection(latestStatus)}
 update();
 };
 tokenButton.onclick=()=>{
 token=prompt('Local control token (memory only)')||'';
 actionStatus.textContent=token?'Control token saved in this tab only; click a non-automatic object to toggle its manual mask.':'Control token entry cancelled.';
 };
 async function mutate(url,body){
 if(mutating){actionStatus.textContent='Selection update already in progress; wait before clicking again.';return false}
 if(!followLatest||!body.frameId||body.frameId!==displayedFrameId){
  actionStatus.textContent='Saved photos are read-only. Show latest before changing masks or discarding.';return false;
 }
 actionStatus.textContent='Preparing selection update…';
 if(!token)token=prompt('Local control token (memory only)')||'';
 if(!token){actionStatus.textContent='No control token entered; selections unchanged.';return false}
 mutating=true;stateGeneration++;blockedFrameId=body.frameId;
 hideSnapshot('Updating selections…');
 if(latestStatus)renderGallery(latestStatus);
 try{const r=await fetch(url,{method:'POST',headers:{Authorization:'Bearer '+token,'Content-Type':'application/json'},body:JSON.stringify(body)});
  const result=await r.json();
  if(!r.ok){if(r.status===401)token='';actionStatus.textContent=result.error;return false}
  actionStatus.textContent=url==='/selection/clear'?
   'Manual selections and photo cleared; automatic policy unchanged. Waiting for a new photo.':
   'Selection updated. Masked objects: '+result.selectedCount+' (automatic: '+result.auto_selected_track_ids.length+').';return true;
  }catch{actionStatus.textContent='Receiver unavailable or response lost; selection outcome unknown. Check status before acting again.';return false}
   finally{mutating=false;controls();update()}
 }
 image.onclick=e=>{
 if(image.hidden||!displayedFrameId)return;
 if(!canEdit()){
  actionStatus.textContent='Saved photos are read-only, or the receiver is busy. Show latest and wait before changing masks.';return;
 }
 const r=image.getBoundingClientRect(),body={x:(e.clientX-r.left)/r.width,y:(e.clientY-r.top)/r.height,frameId:displayedFrameId};
 const px=body.x*image.naturalWidth,py=body.y*image.naturalHeight;
 const best=displayedObjects.filter(({box:b})=>px>=b.x1&&px<=b.x2&&py>=b.y1&&py<=b.y2)
  .sort((a,b)=>(a.box.x2-a.box.x1)*(a.box.y2-a.box.y1)-(b.box.x2-b.box.x1)*(b.box.y2-b.box.y1))[0];
 if(best&&displayedAutoIds.includes(best.track_id)){
  actionStatus.textContent='Automatic '+best.label+' mask stays on by policy; no control token or manual selection is needed.';return;
 }
 mutate('/selection',body);
 };
 clearButton.onclick=async()=>{
 if(!canEdit())return;
 const frameId=displayedFrameId;
 if(confirm('Discard the current photo and clear manual selections? Older saved photos remain; automatic-mask policy stays active.')){
  await mutate('/selection/clear',{frameId});
 }
 };
async function update(){
 if(updating||mutating)return;
 updating=true;const generation=stateGeneration;
 try{const response=await fetch('/status',{cache:'no-store',signal:AbortSignal.timeout(5000)});
 if(!response.ok)throw new Error('Status unavailable');
 const s=await response.json();
 if(mutating||generation!==stateGeneration)return;
 latestStatus=s;blockedFrameId=null;
 policyStatus.textContent='Automatic mask classes: '+(s.auto_mask_classes.join(', ')||'OFF')+
  ' · Auto-masked objects: '+s.auto_selected_track_ids.length;
 status.textContent=
   'Photos: '+s.frames_received+' · Published: '+s.frames_published+' · YOLO: '+(s.detection_latency_ms??'—')+' ms'+
   ' · Masked objects: '+s.selectedCount+' · Manual: '+s.manual_selected_track_ids.length+
    (s.stale?' · STALE: latest photo is old; saved stills remain viewable':'')+
   (s.fail_closed?' · FULL-FRAME MASK / output withheld':'')+(s.detection_error?' · '+s.detection_error:'');
  renderGallery(s);renderSelection(s);
  }catch{if(!mutating&&generation===stateGeneration){
   status.textContent='Receiver unavailable';hideSnapshot('Receiver unavailable; photo hidden.');
   if(latestStatus){blockedFrameId=latestStatus.frame_id;renderGallery(latestStatus)}
  }}
 finally{updating=false}
}
setInterval(update,1000);update();
</script></html>`;

export async function main() {
  // Explicit package-relative .env, independent of the launcher's working directory.
  dotenv.config({ path: fileURLToPath(new URL('../.env', import.meta.url)) });
  const config = readConfig();
  const { YoloDetector } = await import('./vision.js');
  const detector = await YoloDetector.create();
  const service = createApp({ detector, config });
  const server = http.createServer(service.app);
  server.requestTimeout = config.uploadTimeoutMs + 5000;
  server.headersTimeout = 10000;
  server.maxConnections = 32;
  server.listen(config.port, config.host, () => {
    console.log(`[mentra] Receiver on ${config.host}:${config.port}; local viewer http://127.0.0.1:${config.port}/`);
    console.log(config.host === '127.0.0.1'
      ? '[mentra] Loopback only. Use adb reverse for USB uploads; LAN clients cannot connect.'
      : '[mentra] Trusted LAN only. Glasses Wi-Fi uploads need this Mac’s LAN address.');
    console.log('[mentra] In-memory snapshots, local inference, configured automatic/manual masking. No raw-image endpoint.');
  });
  const stop = () => { service.close(); server.close(); server.closeAllConnections(); };
  process.once('SIGINT', stop);
  process.once('SIGTERM', stop);
  return server;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch(() => {
    console.error('[mentra] Startup failed. Check token/automatic-mask classes/limits, local dependencies, model and port.');
    process.exitCode = 1;
  });
}
