// Real chat + activity provider; only HTTP, authentication and audio are controlled.
const assert = require('node:assert/strict');
const { test, after } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const frontend = path.resolve(__dirname, '../../src/Desarrollo.Web/ClientApp');
const { JSDOM } = require(path.join(frontend, 'node_modules/jsdom'));
const ts = require(path.join(frontend, 'node_modules/typescript'));
const dom = new JSDOM('<!doctype html><body></body>', { url: 'http://localhost/' });
global.window = dom.window; global.document = dom.window.document;
Object.defineProperty(global, 'navigator', { value: dom.window.navigator, configurable: true });
global.IS_REACT_ACT_ENVIRONMENT = true;
window.HTMLElement.prototype.scrollIntoView = () => {};
after(() => dom.window.close());
for (const ext of ['.ts', '.tsx']) require.extensions[ext] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { fileName: filename, compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 } });
  module._compile(result.outputText, filename);
};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { MemoryRouter } = require(path.join(frontend, 'node_modules/react-router-dom'));
const { api } = require(path.join(frontend, 'src/api.ts'));
const appPath = path.join(frontend, 'src/App.tsx');
require.cache[appPath] = { id: appPath, filename: appPath, loaded: true, exports: { useAuth: () => ({ user: { id: 'self', organizationId: 'org', isAuthenticated: true } }) } };
const livePath = path.join(frontend, 'src/live/LiveActivityProvider.tsx');
let live;
require.cache[livePath] = { id: livePath, filename: livePath, loaded: true, exports: { useLiveActivity: () => live } };
const ChatPage = require(path.join(frontend, 'src/pages/ChatPage.tsx')).default;
const NotificationsPage = require(path.join(frontend, 'src/pages/NotificationsPage.tsx')).default;
const time = '2026-10-28T12:00:00Z';
const channels = [{ id: 'a', name: 'Canal A', type: 0, isMember: true }, { id: 'b', name: 'Canal B', type: 0, isMember: true }];
function message(id, channelId, body, authorId = 'other') { return { id, channelId, body, authorId, authorName: 'Otra persona', createdAt: time, isEdited: false }; }
let reads, writes, rows;
function snapshot(observedAt = time) { return { observedAt, channels: [], notifications: { unreadCount: 0, items: [] } }; }
function resetChat() {
  reads = []; writes = []; rows = { a: [message('a1', 'a', 'Mensaje inicial A')], b: [message('b1', 'b', 'Mensaje inicial B')] };
  live = { snapshot: snapshot(), chatVersion: 0, notificationVersion: 0, unreadCount: 0, connected: true, accessLost: false,
    soundEnabled: false, soundReady: false, soundError: '', toggleSound: async () => {}, refresh: async () => {} };
  api.get = async (url) => {
    reads.push(url);
    if (url === '/api/channels') return channels;
    if (url === '/api/organizations/members') return [];
    const found = url.match(/^\/api\/channels\/([ab])\/messages/);
    if (found) return { items: rows[found[1]], total: rows[found[1]].length, page: 1, pageSize: 50 };
    throw new Error('Unexpected GET ' + url);
  };
  api.post = async (url, body) => { writes.push({ url, body }); return {}; };
}
async function flush(action) { await act(async () => { await action?.(); await new Promise((resolve) => setImmediate(resolve)); }); }
async function mount(element) {
  const host = document.createElement('div'); document.body.appendChild(host); const root = createRoot(host);
  const render = () => root.render(React.createElement(MemoryRouter, null, React.cloneElement(element)));
  await flush(render);
  return { render: () => flush(render), close: async () => { await flush(() => root.unmount()); host.remove(); } };
}
function button(text) { return [...document.querySelectorAll('button')].find((item) => item.textContent.includes(text)); }
function draft(text) { const input = document.querySelector('[placeholder="Escribe un mensaje…"]'); Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, text); input.dispatchEvent(new window.Event('input', { bubbles: true })); }
function submit() { document.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); }

test('Incoming activity updates the open conversation without navigation or losing its draft', async () => {
  resetChat(); const screen = await mount(React.createElement(ChatPage));
  try {
    assert.match(document.querySelector('.chat-body').textContent, /Mensaje inicial A/);
    await flush(() => draft('Borrador pendiente'));
    rows.a = [...rows.a, message('a2', 'a', 'Nuevo mensaje recibido')];
    live = { ...live, chatVersion: 1 };
    await screen.render();
    assert.match(document.querySelector('.chat-body').textContent, /Nuevo mensaje recibido/);
    assert.equal(document.querySelector('[placeholder="Escribe un mensaje…"]').value, 'Borrador pendiente');
    assert.equal(window.location.pathname, '/');
    assert(reads.filter((url) => url.startsWith('/api/channels/a/messages')).length >= 2);
  } finally { await screen.close(); }
});

test('Late responses from an earlier channel cannot replace the current conversation', async () => {
  resetChat(); const screen = await mount(React.createElement(ChatPage));
  try {
    const get = api.get; let finishOld;
    api.get = (url) => url.startsWith('/api/channels/a/messages') ? new Promise((resolve) => { finishOld = resolve; }) : get(url);
    live = { ...live, chatVersion: 1 }; await screen.render();
    assert.equal(typeof finishOld, 'function');
    await flush(() => button('Canal B').click());
    assert.match(document.querySelector('.chat-body').textContent, /Mensaje inicial B/);
    await flush(() => finishOld({ items: [message('old', 'a', 'Respuesta atrasada A')], total: 1, page: 1, pageSize: 50 }));
    assert.match(document.querySelector('.chat-body').textContent, /Mensaje inicial B/);
    assert.doesNotMatch(document.querySelector('.chat-body').textContent, /Respuesta atrasada A/);
  } finally { await screen.close(); }
});

test('Sending twice while a request waits creates one write and a failure preserves the draft', async () => {
  resetChat(); const screen = await mount(React.createElement(ChatPage));
  try {
    let failSend; const post = api.post;
    api.post = (url, body) => {
      if (!url.endsWith('/messages')) return post(url, body);
      writes.push({ url, body }); return new Promise((resolve, reject) => { failSend = reject; });
    };
    await flush(() => draft('Mensaje que no debo perder'));
    await flush(() => { submit(); submit(); });
    assert.equal(writes.filter((entry) => entry.url.endsWith('/messages')).length, 1);
    await flush(() => failSend(new Error('Sin conexión al enviar')));
    assert.equal(document.querySelector('[placeholder="Escribe un mensaje…"]').value, 'Mensaje que no debo perder');
    assert.match(document.querySelector('[role="alert"]').textContent, /Sin conexión al enviar/);
    assert.equal(button('Enviar').disabled, false);
  } finally { await screen.close(); }
});

test('New notification activity updates the list and marking read refreshes the shared badge state', async () => {
  resetChat(); let refreshes = 0;
  live.refresh = async () => { refreshes++; };
  let notices = [{ id: 'notice-1', event: 0, message: 'Aviso inicial', entityType: 'WorkItem', entityId: 'task-1', isRead: false, createdAt: time }];
  api.get = async (url) => { assert.match(url, /^\/api\/notifications\?/); return { items: notices, total: notices.length, page: 1, pageSize: 30 }; };
  const screen = await mount(React.createElement(NotificationsPage));
  try {
    assert.match(document.body.textContent, /Aviso inicial/);
    notices = [...notices, { ...notices[0], id: 'notice-2', message: 'Nuevo aviso recibido' }];
    live = { ...live, notificationVersion: 1 }; await screen.render();
    assert.match(document.body.textContent, /Nuevo aviso recibido/);
    await flush(() => button('Marcar todo leído').click());
    assert(writes.some((entry) => entry.url === '/api/notifications/read-all'));
    assert(refreshes > 0, 'read changes also update the shared activity/badge');
  } finally { await screen.close(); }
});

test('A previously read notification disappears after permissions change even when unread activity stays the same', async () => {
  resetChat(); let listCalls = 0;
  let notices = [{ id: 'read-private', event: 0, message: 'Aviso privado ya leído', entityType: 'WorkItem', entityId: 'private-task', isRead: true, createdAt: time }];
  api.get = async (url) => { assert.match(url, /^\/api\/notifications\?/); listCalls++; return { items: notices, total: notices.length, page: 1, pageSize: 30 }; };
  const screen = await mount(React.createElement(NotificationsPage));
  try {
    assert.match(document.body.textContent, /Aviso privado ya leído/);
    notices = []; // The authorized listing no longer includes this resource.
    live = { ...live, snapshot: snapshot('2026-10-28T12:00:03Z') };
    assert.equal(live.notificationVersion, 0);
    await screen.render();
    assert(listCalls >= 2, 'each new activity snapshot revalidates the visible notification list');
    assert.doesNotMatch(document.body.textContent, /Aviso privado ya leído/);
    assert.match(document.body.textContent, /Sin notificaciones/);
  } finally { await screen.close(); }
});

test('A direct-chat request resolved after access is revoked cannot repopulate the conversation', async () => {
  resetChat(); const get = api.get;
  api.get = async (url) => url === '/api/organizations/members' ? [{ userId: 'peer', name: 'Persona de prueba', isActive: true }] : get(url);
  const screen = await mount(React.createElement(ChatPage));
  try {
    let finishDirect; const post = api.post;
    api.post = (url, body) => url === '/api/channels/direct' ? new Promise((resolve) => { finishDirect = resolve; }) : post(url, body);
    await flush(() => button('Persona de prueba').click());
    assert.equal(typeof finishDirect, 'function');
    live = { ...live, accessLost: true, chatVersion: 1, snapshot: null };
    await screen.render();
    assert.match(document.querySelector('.chat-head').textContent, /Selecciona una conversación/);
    await flush(() => finishDirect({ id: 'late-direct', name: 'Conversación privada tardía', type: 1, directUserId: 'peer', isMember: true }));
    assert.doesNotMatch(document.body.textContent, /Conversación privada tardía/);
    assert.equal(reads.some((url) => url.includes('/late-direct/messages')), false);
    assert.equal(document.querySelector('.chat-body').textContent.trim(), '');
  } finally { await screen.close(); }
});

// Load the actual provider after ChatPage has captured its controlled context hook.
let audio, currentActivity;
function loadProvider() {
  const soundPath = path.join(frontend, 'src/live/sound.ts');
  require.cache[soundPath] = { id: soundPath, filename: soundPath, loaded: true, exports: { createAlertSound: () => audio } };
  delete require.cache[livePath];
  return require(livePath);
}
async function mountProvider({ enableSound = true } = {}) {
  audio = { played: [], unlocks: 0, closes: 0, unlock: async () => { audio.unlocks++; return true; }, play: (kind) => audio.played.push(kind), close: () => { audio.closes++; } };
  const { LiveActivityProvider, useLiveActivity } = loadProvider();
  function Probe() {
    currentActivity = useLiveActivity();
    return React.createElement('div', null, String(currentActivity.unreadCount),
      React.createElement('button', { 'data-sound-toggle': '', onClick: () => { void currentActivity.toggleSound(); } }, 'Sonido'));
  }
  const screen = await mount(React.createElement(LiveActivityProvider, { userId: 'self', organizationId: 'org' }, React.createElement(Probe)));
  if (enableSound && (!currentActivity.soundEnabled || !currentActivity.soundReady)) await flush(() => currentActivity.toggleSound());
  audio.played.length = 0; // Explicit activation has its own intentional preview tone.
  return screen;
}
function channelActivity(id, messageId, authorId, createdAt) { return { id, latestMessageId: messageId, latestMessageAuthorId: authorId, latestMessageAt: createdAt, changedAt: createdAt, unreadCount: 1 }; }

test('Activity is initially silent, ignores own/history/duplicates, and alerts for new incoming messages and notices', async () => {
  let state = snapshot();
  state.channels = [channelActivity('a', 'old-message', 'other', '2026-10-27T12:00:00Z')];
  state.notifications = { unreadCount: 1, items: [{ id: 'old-notice', createdAt: '2026-10-27T12:00:00Z' }] };
  api.get = async (url) => { assert.equal(url, '/api/activity'); return state; };
  const screen = await mountProvider();
  try {
    assert.deepEqual(audio.played, []); assert.equal(currentActivity.unreadCount, 1);
    state = { ...state, observedAt: '2026-10-28T12:00:03Z', channels: [channelActivity('a', 'new-message', 'other', '2026-10-28T12:00:02Z')] };
    await flush(() => currentActivity.refresh());
    assert.deepEqual(audio.played, ['message']);
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message']);
    state = { ...state, channels: [channelActivity('a', 'own-message', 'self', '2026-10-28T12:00:04Z')] };
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message']);
    state = { ...state, channels: [...state.channels, channelActivity('historical', 'past-grant', 'other', '2026-10-27T12:00:00Z')] };
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message']);
    state = { ...state, notifications: { unreadCount: 2, items: [...state.notifications.items, { id: 'new-notice', createdAt: '2026-10-28T12:00:05Z' }] } };
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message', 'notification']);
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message', 'notification']);
  } finally { await screen.close(); }
  assert.equal(audio.closes, 1);
});

test('A later outgoing message does not hide a new incoming-message sound between activity checks', async () => {
  let state = { ...snapshot(), channels: [{ ...channelActivity('a', 'own-old', 'self', '2026-10-27T12:00:02Z'), latestIncomingMessageId: 'incoming-old', latestIncomingMessageAt: '2026-10-27T12:00:01Z' }] };
  api.get = async () => state;
  const screen = await mountProvider();
  try {
    assert.deepEqual(audio.played, []);
    state = { ...state, observedAt: '2026-10-28T12:00:03Z', channels: [{ ...channelActivity('a', 'own-new', 'self', '2026-10-28T12:00:02Z'), latestIncomingMessageId: 'incoming-new', latestIncomingMessageAt: '2026-10-28T12:00:01Z' }] };
    await flush(() => currentActivity.refresh());
    assert.deepEqual(audio.played, ['message'], 'the incoming cursor is independent of the newest message author');
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message']);
    state = { ...state, channels: [{ ...state.channels[0], latestMessageId: 'own-newer', latestMessageAt: '2026-10-28T12:00:04Z' }] };
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message'], 'later own messages do not replay the incoming cursor');
  } finally { await screen.close(); }
});

test('Muted incoming activity is not replayed when sound is enabled again', async () => {
  let state = snapshot(); api.get = async () => state;
  const screen = await mountProvider();
  try {
    await flush(() => currentActivity.toggleSound()); assert.equal(currentActivity.soundEnabled, false);
    state = { ...state, channels: [channelActivity('a', 'muted-message', 'other', '2026-10-28T12:00:01Z')] };
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, []);
    await flush(() => currentActivity.toggleSound()); assert.equal(currentActivity.soundEnabled, true);
    audio.played.length = 0; // Ignore the user's explicit activation preview.
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, []);
    state = { ...state, channels: [channelActivity('a', 'audible-message', 'other', '2026-10-28T12:00:02Z')] };
    await flush(() => currentActivity.refresh()); assert.deepEqual(audio.played, ['message']);
  } finally { await screen.close(); }
});

test('Online recovery refreshes activity and revoked access clears its snapshot', async () => {
  let calls = 0, revoked = false;
  api.get = async () => { calls++; if (revoked) throw Object.assign(new Error('Acceso retirado'), { status: 403 }); return snapshot(); };
  const screen = await mountProvider();
  try {
    const before = calls; await flush(() => window.dispatchEvent(new window.Event('online')));
    assert(calls > before, 'recovering connectivity rechecks without reloading the page');
    revoked = true; await flush(() => currentActivity.refresh());
    assert.equal(currentActivity.accessLost, true); assert.equal(currentActivity.snapshot, null);
    assert.equal(currentActivity.unreadCount, 0); assert.deepEqual(audio.played, []);
  } finally { await screen.close(); }
});

test('The sound button activates on its first click even after the pointerdown microtasks finish', async () => {
  window.localStorage.removeItem('crm:sound:org:self');
  api.get = async () => snapshot();
  const screen = await mountProvider({ enableSound: false });
  try {
    const toggle = document.querySelector('[data-sound-toggle]');
    assert.equal(currentActivity.soundReady, false);
    await flush(() => toggle.dispatchEvent(new window.Event('pointerdown', { bubbles: true })));
    assert.equal(audio.unlocks, 0, 'the control handles its own activation instead of racing the global pointer listener');
    await flush(() => toggle.click());
    assert.equal(currentActivity.soundEnabled, true); assert.equal(currentActivity.soundReady, true);
    assert.equal(audio.unlocks, 1);
    await flush(() => toggle.dispatchEvent(new window.Event('pointerdown', { bubbles: true })));
    await flush(() => toggle.click());
    assert.equal(currentActivity.soundEnabled, false, 'the following click silences the now-active sound');
  } finally { await screen.close(); }
});

test('Unmount aborts the pending activity read and late results cannot play a sound', async () => {
  api.get = async () => snapshot();
  const screen = await mountProvider();
  let finishRead, readSignal;
  api.get = (url, signal) => { readSignal = signal; return new Promise((resolve) => { finishRead = resolve; }); };
  await flush(() => { void currentActivity.refresh(); });
  assert.equal(readSignal.aborted, false);
  await screen.close();
  assert.equal(readSignal.aborted, true); assert.equal(audio.closes, 1);
  await flush(() => finishRead({ ...snapshot('2026-10-28T12:00:03Z'), channels: [channelActivity('a', 'too-late', 'other', '2026-10-28T12:00:02Z')] }));
  assert.deepEqual(audio.played, [], 'the previous account/session cannot alert after leaving its shell');
});
