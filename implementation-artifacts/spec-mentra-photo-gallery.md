---
title: 'In-memory gallery of the last 20 masked photos'
type: 'feature'
created: '2026-11-15'
status: 'done'
context:
  - '{project-root}/CLAUDE.md'
  - '{project-root}/Mentra/README.md'
---

## Approved intent

The user requested a thumbnail gallery of the last 20 masked photos, kept in
memory without saving raw photos to disk. The existing latest-photo preview
must continue following new captures unless the user chooses an older photo.
The user explicitly prohibited review workflows and tests.

## Boundaries

- Retain only processed JPEGs and thumbnails for history; do not retain raw
  historical photos or write image files, localStorage, or IndexedDB.
- Bound history to at most 20 source photos and 64 MiB, including thumbnails;
  evict oldest entries first if either limit is reached.
- Preserve automatic TV/laptop masking before every first publication,
  authentication, current-source identity checks, and manual privacy fallback.
- Re-rendering masks updates the same photo entry rather than adding a duplicate.
  Invalidate that entry while re-rendering or on failure so an older, less-masked
  version is not served as the current result.
- History starts with future captures and resets when the receiver restarts.
  No automatic captures, firmware changes, secret logging, commits, tests,
  review agents, or unrelated integration changes.

## Tasks

- [x] `Mentra/laptop/src/photo-history.js` — implement a bounded in-memory
  processed-photo store with source-frame identity, revision, capture metadata,
  and small thumbnails derived only from already-processed JPEGs.
- [x] `Mentra/laptop/src/pipeline.js` — record successful processed renders,
  update the current photo on mask edits, invalidate withheld/failed current
  entries, and expose safe gallery metadata. Keep one raw working frame only.
- [x] `Mentra/laptop/src/server.js` — serve stored processed full images and
  thumbnails using exact in-memory IDs, no-store headers, and no filesystem
  image routes. Unknown/evicted entries must not fall back to raw/latest images.
- [x] `Mentra/laptop/src/server.js` — add a bounded thumbnail strip/grid, selected
  image state, and Show latest. New captures update thumbnails without stealing
  focus from an older selected photo.
- [x] `Mentra/laptop/src/server.js` — keep historical previews read-only; manual
  masking and current-photo discard require the latest source photo. Hide or
  invalidate stale current thumbnails during mask mutations.
- [x] `Mentra/README.md` — document memory/byte limits, eviction, no image-file
  persistence, history start/reset, and current versus historical selection.
- [x] Restart only the scoped idle receiver, leaving Mac app/.env/automatic-mask
  policy intact. Confirm health and gallery metadata without taking photos.

## Acceptance

- Given successful captures, when processed output is published, then the browser
  lists up to the most recent 20 masked photos and can display a selected one.
- Given mask edits to the latest photo, when rendering completes, then its
  gallery entry updates rather than creating another captured-photo entry.
- Given rendering is withheld or fails, when a historical route requests that
  current photo, then its earlier less-masked representation is unavailable.
- Given a selected older photo, when a new capture arrives, then the old preview
  stays selected and the new thumbnail appears; Show latest restores following.
- Given an evicted/cleared photo, when requested, then return no image and show a
  clear UI message rather than silently substituting another source photo.
- Given source-frame identity mismatch or historical browsing, when masking or
  discard is attempted, then no different source photo is changed.
- Given no photos yet, when the receiver starts, then show an empty gallery and
  keep capture manual. No test/review workflows are run.

## Deployment and checks

Use only source/syntax checks, normal receiver startup and safe health/status
reads. Do not capture, synthesize, save, or upload photos for verification.
Leave browser confirmation to the user. Receiver restart clears existing
in-memory photos; initial gallery is empty by design.

Implementation and parent deployment are complete.
Syntax-only checks passed for `photo-history.js`, `pipeline.js`, `server.js`, and
the embedded viewer JavaScript via stdin. No tests, reviews, receiver restart,
health/API calls, browser automation, or photo captures were performed.

Parent deployment: the scoped idle receiver was restarted gracefully, without
changing the Mac app or `.env`. Health reports the model ready. Live status
confirms automatic classes `tv,laptop` and a gallery limit of 20 / 64 MiB.
The gallery starts empty after restart and the updated page serves the gallery
and Show latest controls. No review/test workflows, browser automation, image
captures, or image uploads were run. Visual gallery confirmation remains for
the user's next captures.
