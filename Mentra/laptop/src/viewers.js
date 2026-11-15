export class Viewers {
  constructor({ maxViewers = 8, maxFrameBytes = 5242880, drainTimeoutMs = 5000 } = {}) {
    Object.assign(this, { maxViewers, maxFrameBytes, drainTimeoutMs });
    this.clients = new Set();
    this.timers = new Map();
  }

  remove(response) {
    clearTimeout(this.timers.get(response));
    this.timers.delete(response);
    this.clients.delete(response);
  }

  add(response, latest) {
    if (this.clients.size >= this.maxViewers) return false;
    response.writeHead(200, { 'Content-Type': 'multipart/x-mixed-replace; boundary=frame',
      'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
    this.clients.add(response);
    response.once('close', () => this.remove(response));
    response.once('error', () => { this.remove(response); response.destroy(); });
    if (latest) this.send(response, latest);
    return true;
  }

  send(response, jpeg) {
    if (response.destroyed || response.writableEnded || response.writableNeedDrain
      || this.timers.has(response) || jpeg.length > this.maxFrameBytes) {
      this.remove(response);
      response.destroy();
      return;
    }
    const chunk = Buffer.concat([Buffer.from(
      `--frame\r\nContent-Type: image/jpeg\r\nContent-Length: ${jpeg.length}\r\n\r\n`,
    ), jpeg, Buffer.from('\r\n')]);
    // One bounded write per client, never a per-viewer frame queue.
    if (!response.write(chunk)) {
      const timer = setTimeout(() => { this.remove(response); response.destroy(); }, this.drainTimeoutMs);
      timer.unref?.();
      this.timers.set(response, timer);
      response.once('drain', () => {
        clearTimeout(this.timers.get(response));
        this.timers.delete(response);
      });
    }
  }

  publish(jpeg) {
    for (const response of this.clients) this.send(response, jpeg);
  }

  close() {
    for (const response of this.clients) { this.remove(response); response.destroy(); }
  }
}
