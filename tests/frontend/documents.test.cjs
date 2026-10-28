const assert = require('node:assert/strict');
const { test, after } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const frontend = path.resolve(__dirname, '../../src/Desarrollo.Web/ClientApp');
const { JSDOM } = require(path.join(frontend, 'node_modules/jsdom'));
const ts = require(path.join(frontend, 'node_modules/typescript'));
const dom = new JSDOM('<!doctype html><body></body>', { url: 'http://localhost/', pretendToBeVisual: true });
global.window = dom.window; global.document = dom.window.document;
Object.defineProperty(global, 'navigator', { value: dom.window.navigator, configurable: true });
for (const name of ['HTMLElement', 'Node', 'DOMParser', 'MutationObserver', 'getComputedStyle']) global[name] = dom.window[name];
global.requestAnimationFrame = (callback) => setTimeout(callback, 0);
global.cancelAnimationFrame = clearTimeout;
global.innerHeight = 900; global.innerWidth = 1280;
dom.window.Range.prototype.getClientRects = () => [];
dom.window.Range.prototype.getBoundingClientRect = () => ({ top: 0, bottom: 0, left: 0, right: 0, width: 0, height: 0 });
global.IS_REACT_ACT_ENVIRONMENT = true;
const documentStyle = document.createElement('style');
documentStyle.textContent = fs.readFileSync(path.join(frontend, 'src/pages/documents.css'), 'utf8');
document.head.appendChild(documentStyle);
after(() => dom.window.close());
for (const ext of ['.ts', '.tsx']) require.extensions[ext] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { fileName: filename, compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 } });
  module._compile(result.outputText, filename);
};
require.extensions['.css'] = () => {};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { createMemoryRouter, RouterProvider } = require(path.join(frontend, 'node_modules/react-router-dom'));
const { api } = require(path.join(frontend, 'src/api.ts'));
const { NavigationGuardProvider } = require(path.join(frontend, 'src/components/NavigationGuard.tsx'));
const appPath = path.join(frontend, 'src/App.tsx');
require.cache[appPath] = { id: appPath, filename: appPath, loaded: true, exports: { useAuth: () => ({ user: { id: 'owner' } }) } };
const DocumentsPage = require(path.join(frontend, 'src/pages/DocumentsPage.tsx')).default;
const initial = { id: 'doc-1', projectId: 'p1', title: 'Acuerdo', authorId: 'owner', tags: [], updatedAt: '2026-10-27T16:00:00Z', content: 'Texto de prueba', contentFormat: 'plain', visibility: 2, readerIds: [], canEdit: true, canManageAccess: true, rowVersion: 'v1' };
let documents, writes, failure, latest;
function reset() {
  documents = [{ ...initial }, { ...initial, id: 'doc-2', title: 'Segunda nota' }]; writes = []; failure = null; latest = null;
  api.get = async (url) => {
    if (url.startsWith('/api/projects?')) return { items: [{ id: 'p1', name: 'Proyecto de prueba' }] };
    if (url.endsWith('/members')) return [{ userId: 'owner', name: 'Autor', isActive: true }, { userId: 'reader-1', name: 'Ana', isActive: true }];
    if (url.startsWith('/api/documents?')) return documents;
    if (url.startsWith('/api/documents/')) return latest || documents.find((entry) => url.endsWith('/' + entry.id));
    throw new Error('Unexpected GET ' + url);
  };
  const write = async (url, payload) => {
    writes.push({ url, payload }); if (failure) throw Object.assign(new Error(failure.message || 'No se pudo guardar'), { status: failure.status || 500 });
    const existing = documents.find((entry) => url.endsWith('/' + entry.id));
    const result = { ...initial, ...existing, ...payload, id: existing?.id || 'doc-created', projectId: 'p1', rowVersion: 'v-next' };
    documents = [...documents.filter((entry) => entry.id !== result.id), result]; latest = null; return result;
  };
  api.put = api.post = write; window.confirm = () => true;
}
async function flush(action) { await act(async () => { await action?.(); await new Promise((resolve) => setTimeout(resolve, 15)); }); }
function byText(text) { return [...document.querySelectorAll('button')].find((button) => button.textContent.trim() === text); }
function tree(title) { return [...document.querySelectorAll('.documents-tree-item')].find((button) => button.textContent.includes(title)); }
function setTitle(title) { const input = document.querySelector('[aria-label="Título del documento"]'); Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, title); input.dispatchEvent(new window.Event('input', { bubbles: true })); }
async function mount() {
  const container = document.createElement('div'); document.body.appendChild(container);
  const router = createMemoryRouter([{ path: '/docs', element: React.createElement(NavigationGuardProvider, null, React.createElement(DocumentsPage)) }, { path: '/other', element: React.createElement('div', null, 'Other') }], { initialEntries: ['/docs'] });
  const root = createRoot(container); await flush(() => root.render(React.createElement(React.StrictMode, null, React.createElement(RouterProvider, { router }))));
  return { router, close: async () => { await flush(() => root.unmount()); router.dispose(); container.remove(); } };
}

test('Real rich editor formats and saves JSON; legacy text is never interpreted as HTML', { timeout: 15000 }, async () => {
  reset(); documents[0].content = '<script>alert(1)</script> Texto'; const screen = await mount();
  try {
    await flush(() => tree('Acuerdo').click());
    assert.equal(document.querySelector('.tiptap script'), null);
    assert.match(document.querySelector('.tiptap').textContent, /<script>alert\(1\)<\/script>/);
    const editor = document.querySelector('.tiptap').editor;
    await flush(() => editor.commands.selectAll());
    await flush(() => document.querySelector('[aria-label="Negrita"]').click());
    await flush(() => document.querySelector('[aria-label="Cursiva"]').click());
    await flush(() => document.querySelector('[aria-label="Encabezado 1"]').click());
    await flush(() => document.querySelector('[aria-label="Centrar texto"]').click());
    assert(document.querySelector('.tiptap h1 strong em, .tiptap h1 em strong'));
    assert.equal(document.querySelector('.tiptap h1').style.textAlign, 'center');
    await flush(() => byText('Guardar documento').click());
    const payload = writes.at(-1).payload, content = JSON.parse(payload.content);
    assert.equal(payload.contentFormat, 'tiptap'); assert.equal(payload.rowVersion, 'v1');
    assert.equal(content.content[0].type, 'heading'); assert.equal(content.content[0].attrs.textAlign, 'center');
    assert.deepEqual(content.content[0].content[0].marks.map((mark) => mark.type).sort(), ['bold', 'italic']);
    await flush(() => tree('Segunda nota').click());
    await flush(() => tree('Acuerdo').click());
    assert(document.querySelector('.tiptap h1 strong em, .tiptap h1 em strong'), 'format survives reopening saved JSON: ' + JSON.stringify({ title: document.querySelector('[aria-label="Título del documento"]')?.value, html: document.querySelector('.tiptap')?.innerHTML, saved: documents.find((entry) => entry.id === 'doc-1')?.content, writes: writes.length }));
  } finally { await screen.close(); }
});

test('New notes start private and selected sharing sends only explicitly chosen readers; checklists persist', { timeout: 15000 }, async () => {
  reset(); const screen = await mount();
  try {
    await flush(() => byText('+ Nuevo documento').click());
    assert.equal(document.querySelector('input[name="document-visibility"]:checked').value, '2');
    await flush(() => setTitle('Lista de entrega'));
    const editor = document.querySelector('.tiptap').editor;
    await flush(() => editor.commands.insertContent('Validar versión'));
    await flush(() => document.querySelector('[aria-label="Lista de verificación"]').click());
    assert(document.querySelector('.tiptap ul[data-type="taskList"]'));
    const taskItem = document.querySelector('.tiptap ul[data-type="taskList"] > li');
    assert(taskItem.querySelector(':scope > label input[type="checkbox"]'));
    assert(taskItem.querySelector(':scope > div p'));
    assert.equal(window.getComputedStyle(taskItem).display, 'flex', 'actual Tiptap task item places checkbox and text side by side');
    assert.equal(window.getComputedStyle(taskItem.querySelector(':scope > div')).flex, '1 1 0%', 'task text occupies the remaining line');
    await flush(() => byText('Guardar documento').click());
    assert.equal(writes.at(-1).payload.visibility, 2); assert.deepEqual(writes.at(-1).payload.readerIds, []);
    assert.equal(JSON.parse(writes.at(-1).payload.content).content[0].type, 'taskList');
    await flush(() => document.querySelector('input[name="document-visibility"][value="1"]').click());
    await flush(() => byText('Guardar documento').click());
    assert.match(document.querySelector('[role="alert"]').textContent, /Selecciona al menos una persona/);
    await flush(() => [...document.querySelectorAll('.document-readers label')].find((label) => label.textContent === 'Ana').querySelector('input').click());
    await flush(() => byText('Guardar documento').click());
    assert.equal(writes.at(-1).payload.visibility, 1); assert.deepEqual(writes.at(-1).payload.readerIds, ['reader-1']);
  } finally { await screen.close(); }
});

test('Failed saves block navigation and retain draft; conflict comparison requires explicit retry with latest version', { timeout: 15000 }, async () => {
  reset(); const screen = await mount();
  try {
    await flush(() => tree('Acuerdo').click()); await flush(() => setTitle('Mi borrador'));
    failure = { status: 500, message: 'Sin conexión' };
    await flush(() => { void screen.router.navigate('/other'); });
    assert.equal(screen.router.state.location.pathname, '/docs');
    assert.equal(document.querySelector('[aria-label="Título del documento"]').value, 'Mi borrador');
    failure = { status: 409, message: 'Documento modificado' }; latest = { ...initial, title: 'Versión de otra persona', content: 'Cambio remoto', rowVersion: 'v2' };
    await flush(() => byText('Guardar documento').click());
    assert.match(document.querySelector('[aria-label="Conflicto de documento"]').textContent, /Cambio remoto/);
    assert.equal(document.querySelector('[aria-label="Título del documento"]').value, 'Mi borrador');
    await flush(() => byText('Conservar mi borrador y preparar reintento').click());
    failure = null; await flush(() => byText('Guardar documento').click());
    assert.equal(writes.at(-1).payload.rowVersion, 'v2'); assert.equal(writes.at(-1).payload.title, 'Mi borrador');
  } finally { await screen.close(); }
});

test('Read-only documents cannot change content or audience; stale document responses cannot replace a later selection', { timeout: 15000 }, async () => {
  reset(); documents[1] = { ...documents[1], canEdit: false, canManageAccess: false };
  const get = api.get; let resolveFirst;
  api.get = (url) => url === '/api/documents/doc-1' ? new Promise((resolve) => { resolveFirst = resolve; }) : get(url);
  const screen = await mount();
  try {
    await flush(() => tree('Acuerdo').click()); await flush(() => tree('Segunda nota').click());
    await flush(() => resolveFirst(documents[0]));
    assert.equal(document.querySelector('[aria-label="Título del documento"]').value, 'Segunda nota');
    assert.equal(document.querySelector('[aria-label="Título del documento"]').disabled, true);
    assert.equal(document.querySelector('.tiptap').editor.isEditable, false);
    assert.equal(document.querySelector('input[name="document-visibility"]'), null);
    assert.equal(byText('Guardar documento'), undefined); assert.equal(writes.length, 0);
  } finally { await screen.close(); }
});
