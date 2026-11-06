/* Only active download names and byte counts leave this extension, through local native messaging. */
let port = null, interval = null, busy = false, generation = 0, stopped = true;
let pendingHandshake = false, active = false, retry = null, attempts = 0, lastStatus = '';
const source = /Edg\//.test(navigator.userAgent) ? 'Edge' : 'Chrome';
const ready = chrome.storage.local.get('stopped').then(value => { stopped = value.stopped === true; });
function status(message) {
  if (lastStatus === message) return;
  lastStatus = message;
  chrome.storage.local.set({status: message}).catch(() => {});
}
function disconnect() {
  generation++;
  if (interval) clearInterval(interval);
  if (retry) clearTimeout(retry);
  interval = null;
  retry = null;
  pendingHandshake = false;
  const previous = port; port = null;
  if (previous) previous.disconnect();
}
function connect() {
  if (port) return port;
  const next = chrome.runtime.connectNative('de.iris.downloads'); port = next;
  next.onDisconnect.addListener(() => {
    const message = chrome.runtime.lastError?.message;
    if (port !== next) return;
    port = null; generation++;
    if (interval) clearInterval(interval);
    interval = null;
    if (message) status('Nicht verbunden: ' + message);
    if (active && !stopped && ++attempts <= 3) {
      retry = setTimeout(() => { retry = null; update(); }, 2500);
    }
  });
  next.onMessage.addListener(message => {
    if (port === next && message.ok) { attempts = 0; status('Mit Iris verbunden · nur aktive Downloads'); }
  });
  return next;
}
async function update(force = false) {
  await ready;
  if (stopped) return;
  if (busy) { pendingHandshake ||= force; return; }
  busy = true;
  const current = generation;
  try {
    const downloads = await chrome.downloads.search({state: 'in_progress'});
    if (current !== generation || stopped) return;
    const items = downloads.filter(d => !d.paused && !d.error && Number.isFinite(d.bytesReceived)
      && !(d.totalBytes > 0 && d.bytesReceived >= d.totalBytes)).slice(0, 64).map(d => ({
      name: (d.filename || '').split(/[\\/]/).pop().slice(0, 100),
      received: Math.max(0, d.bytesReceived), total: Number.isFinite(d.totalBytes) && d.totalBytes > 0 ? d.totalBytes : -1
    }));
    active = items.length > 0;
    if (items.length || force || port) {
      const connection = connect();
      connection.postMessage({source, items});
      if (items.length && !interval) interval = setInterval(() => update(), 750);
      if (!items.length) {
        if (interval) clearInterval(interval); interval = null;
        // Keep the handshake alive long enough to receive an error/ack, then let the worker sleep.
        setTimeout(() => { if (!interval && port === connection) disconnect(); }, 1500);
      }
    }
  } catch (error) { status('Verbindung fehlgeschlagen: ' + error.message); disconnect(); }
  finally {
    busy = false;
    if (pendingHandshake) { pendingHandshake = false; update(true); }
  }
}
chrome.downloads.onCreated.addListener(() => update());
chrome.downloads.onChanged.addListener(() => update());
chrome.downloads.onErased.addListener(() => update());
chrome.runtime.onStartup.addListener(() => update(true));
chrome.runtime.onInstalled.addListener(() => update(true));
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id) return;
  if (message.action !== 'connect' && message.action !== 'disconnect') return;
  ready.then(async () => {
    stopped = message.action === 'disconnect';
    await chrome.storage.local.set({stopped});
    if (stopped) { disconnect(); status('Getrennt'); }
    else { attempts = 0; update(true); }
    sendResponse({ok: true});
  }).catch(error => sendResponse({ok: false, error: error.message}));
  return true;
});
