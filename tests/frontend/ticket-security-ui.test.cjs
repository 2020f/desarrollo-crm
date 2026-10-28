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
global.prompt = () => 'Evaluación del equipo';
after(() => dom.window.close());
for (const ext of ['.ts', '.tsx']) require.extensions[ext] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { fileName: filename, compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 } });
  module._compile(result.outputText, filename);
};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { createMemoryRouter, RouterProvider } = require(path.join(frontend, 'node_modules/react-router-dom'));
const { api } = require(path.join(frontend, 'src/api.ts'));
const TicketDetailPage = require(path.join(frontend, 'src/pages/TicketDetailPage.tsx')).default;
const ticket = { id: 'a', code: 'SOL-1', title: 'Solicitud A', category: 0, severity: 1, priority: 1, status: 0, projectId: 'p1', createdAt: '2026-10-28T12:00:00Z', canAssign: false, canChangeStatus: false, canComment: false, canReadInternalNotes: false, canWriteInternalNotes: false };
let record, reads, writes, rejectComment;
function reset(flags = {}) {
  record = { ...ticket, ...flags }; reads = []; writes = []; rejectComment = false;
  api.get = async (url) => {
    reads.push(url);
    if (url.endsWith('/comments')) return [{ id: 'comment-1', body: 'Respuesta compartida', authorName: 'Equipo', createdAt: ticket.createdAt, isInternal: false }];
    if (url.endsWith('/members')) return [{ userId: 'active', name: 'Persona activa', isActive: true }, { userId: 'inactive', name: 'Persona retirada', isActive: false }];
    return record;
  };
  api.post = async (url, payload) => { writes.push({ url, payload }); if (rejectComment && url.endsWith('/comments')) throw new Error('No se pudo enviar el comentario'); return {}; };
}
async function flush(action) { await act(async () => { await action?.(); await new Promise((resolve) => setImmediate(resolve)); }); }
function byText(text) { return [...document.querySelectorAll('button')].find((button) => button.textContent.trim() === text); }
function value(selector, text) {
  const input = document.querySelector(selector); const prototype = input instanceof window.HTMLSelectElement ? window.HTMLSelectElement.prototype : window.HTMLTextAreaElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, 'value').set.call(input, text); input.dispatchEvent(new window.Event(input instanceof window.HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
}
async function mount() {
  const container = document.createElement('div'); document.body.appendChild(container);
  const router = createMemoryRouter([{ path: '/tickets/:id', element: React.createElement(TicketDetailPage) }], { initialEntries: ['/tickets/a'] });
  const root = createRoot(container); await flush(() => root.render(React.createElement(RouterProvider, { router })));
  return { router, close: async () => { await flush(() => root.unmount()); router.dispose(); container.remove(); } };
}

test('Reader flags expose shared comments without administrative controls, internal fields or membership requests', async () => {
  reset(); const screen = await mount();
  try {
    assert.match(document.body.textContent, /Respuesta compartida/); assert.match(document.body.textContent, /solo lectura/);
    assert.equal(document.querySelector('[aria-label="Asignar solicitud"]'), null); assert.equal(byText('En evaluación'), undefined);
    assert.equal(document.querySelector('form'), null); assert.equal(document.querySelector('input[type="checkbox"]'), null);
    assert.equal(reads.some((url) => url.endsWith('/members')), false); assert.deepEqual(writes, []);
  } finally { await screen.close(); }
});

test('Each administrative action is offered only by server capability flags; inactive assignees are excluded', async () => {
  reset({ canAssign: true, canChangeStatus: true, canComment: true, canWriteInternalNotes: true }); const screen = await mount();
  try {
    assert(document.querySelector('[aria-label="Asignar solicitud"]')); assert(document.querySelector('input[type="checkbox"]'));
    assert.doesNotMatch(document.querySelector('[aria-label="Asignar solicitud"]').textContent, /Persona retirada/);
    await flush(() => value('[aria-label="Asignar solicitud"]', 'active'));
    assert.deepEqual(writes.at(-1), { url: '/api/tickets/a/assign', payload: { assigneeId: 'active' } });
    await flush(() => byText('En evaluación').click());
    assert.deepEqual(writes.at(-1), { url: '/api/tickets/a/status', payload: { status: 1, reason: 'Evaluación del equipo' } });
  } finally { await screen.close(); }
});

test('Comment failures keep draft and internal selection; public commenters can only submit shared comments', async () => {
  reset({ canComment: true, canWriteInternalNotes: true }); let screen = await mount();
  try {
    await flush(() => value('[aria-label="Comentario de la solicitud"]', 'Borrador que debe conservarse'));
    await flush(() => document.querySelector('input[type="checkbox"]').click());
    rejectComment = true;
    await flush(() => document.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.match(document.querySelector('[role="alert"]').textContent, /No se pudo enviar/);
    assert.equal(document.querySelector('textarea').value, 'Borrador que debe conservarse'); assert.equal(document.querySelector('input[type="checkbox"]').checked, true);
    assert.equal(writes.at(-1).payload.isInternal, true);
  } finally { await screen.close(); }
  reset({ canComment: true, canWriteInternalNotes: false }); screen = await mount();
  try {
    await flush(() => value('[aria-label="Comentario de la solicitud"]', 'Respuesta pública'));
    assert.equal(document.querySelector('input[type="checkbox"]'), null);
    await flush(() => document.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.deepEqual(writes.at(-1), { url: '/api/tickets/a/comments', payload: { body: 'Respuesta pública', isInternal: false } });
    assert.equal(document.querySelector('textarea').value, '');
  } finally { await screen.close(); }
});

test('Changing ticket identity drops the previous editor and ignores stale responses and privileges', { timeout: 8000 }, async () => {
  reset({ canAssign: true, canChangeStatus: true, canComment: true, canWriteInternalNotes: true }); const screen = await mount();
  try {
    await flush(() => value('[aria-label="Comentario de la solicitud"]', 'Borrador exclusivo de A'));
    const get = api.get; let resolveA, resolveB;
    api.get = (url) => url === '/api/tickets/b' ? new Promise((resolve) => { resolveB = resolve; })
      : url === '/api/tickets/a' ? new Promise((resolve) => { resolveA = resolve; }) : get(url);
    await flush(() => value('[aria-label="Asignar solicitud"]', 'active'));
    await flush(() => { void screen.router.navigate('/tickets/b'); });
    assert.equal(!!document.querySelector('textarea'), false, 'previous ticket editor must not target the new ticket while it loads');
    assert.equal(!!document.querySelector('[aria-label="Asignar solicitud"]'), false, 'previous ticket capabilities must not remain while new authorization loads');
    record = { ...ticket, id: 'b', title: 'Solicitud B', canComment: true };
    await flush(() => resolveB(record));
    await flush(() => resolveA({ ...ticket, title: 'Respuesta atrasada A', canAssign: true, canComment: true }));
    assert.match(document.querySelector('h1').textContent, /Solicitud B/);
    assert.equal(document.querySelector('textarea').value, '', 'draft from another ticket is not silently copied into the new audience');
    assert.equal(document.querySelector('input[type="checkbox"]'), null);
  } finally { await screen.close(); }
});
