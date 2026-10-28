const assert = require('node:assert/strict');
const { test, after } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const frontend = path.resolve(__dirname, '../../src/Desarrollo.Web/ClientApp');
const { JSDOM } = require(path.join(frontend, 'node_modules/jsdom'));
const ts = require(path.join(frontend, 'node_modules/typescript'));
const dom = new JSDOM('<!doctype html><body></body>', { url: 'http://localhost/' });
global.window = dom.window; global.document = dom.window.document; global.FormData = dom.window.FormData;
Object.defineProperty(global, 'navigator', { value: dom.window.navigator, configurable: true });
global.IS_REACT_ACT_ENVIRONMENT = true;
after(() => dom.window.close());
for (const ext of ['.ts', '.tsx']) require.extensions[ext] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { fileName: filename, compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 } });
  module._compile(result.outputText, filename);
};
require.extensions['.css'] = () => {};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const apiModule = require(path.join(frontend, 'src/api.ts'));
const { api } = apiModule, realUpload = api.upload;
const FilesPage = require(path.join(frontend, 'src/pages/FilesPage.tsx')).default;
const pdf = { id: 'f1', storedFileId: 'f1', code: 'FILE-ABC', name: 'Manual.pdf', contentType: 'application/pdf', sizeBytes: 128, projectId: 'p1', visibility: 'Public', uploaderId: 'owner', createdAt: '2026-10-27T16:00:00Z', canDelete: true };
const image = { ...pdf, id: 'f2', storedFileId: 'f2', code: 'FILE-IMG', name: 'Captura.png', contentType: 'image/png', canDelete: false };
let requests, uploads, deletes, failUpload, failList, failDelete, total;
function reset() {
  requests = []; uploads = []; deletes = []; failUpload = false; failList = false; failDelete = false; total = 2;
  api.get = async (url) => {
    if (url.startsWith('/api/projects?')) return { items: [{ id: 'p1', name: 'Proyecto uno' }] };
    if (url.startsWith('/api/files?')) { requests.push(url); if (failList) throw new Error('Sin conexión'); const query = new URL(url, 'http://localhost').searchParams; return { items: [{ ...pdf, visibility: query.get('visibility') }, image], total, page: Number(query.get('page')), pageSize: 50 }; }
    throw new Error('Unexpected GET ' + url);
  };
  api.upload = async (url, body) => { uploads.push({ url, body }); if (failUpload) throw new Error('Carga interrumpida'); return pdf; };
  api.del = async (url) => { deletes.push(url); if (failDelete) throw new Error('Sin permiso'); };
  Object.defineProperty(navigator, 'clipboard', { value: { writeText: async () => {} }, configurable: true });
  window.confirm = () => true;
}
async function flush(action) { await act(async () => { await action?.(); await new Promise((resolve) => setTimeout(resolve, 10)); }); }
function byText(text) { return [...document.querySelectorAll('button')].find((button) => button.textContent.trim() === text); }
function choose(file) { const input = document.querySelector('input[type="file"]'); Object.defineProperty(input, 'files', { configurable: true, value: [file] }); input.dispatchEvent(new window.Event('change', { bubbles: true })); }
function value(selector, text) { const input = document.querySelector(selector); Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, text); input.dispatchEvent(new window.Event('input', { bubbles: true })); }
async function mount() {
  const container = document.createElement('div'); document.body.appendChild(container); const root = createRoot(container);
  await flush(() => root.render(React.createElement(FilesPage)));
  return async () => { await flush(() => root.unmount()); container.remove(); };
}

test('Public folder is scoped to project, private folder omits project; search and pagination are real queries', { timeout: 15000 }, async () => {
  reset(); total = 51; const close = await mount();
  try {
    let query = new URL(requests.at(-1), 'http://localhost').searchParams;
    assert.equal(query.get('visibility'), 'Public'); assert.equal(query.get('projectId'), 'p1');
    await flush(() => byText('Privada').click());
    query = new URL(requests.at(-1), 'http://localhost').searchParams;
    assert.equal(query.get('visibility'), 'Private'); assert.equal(query.has('projectId'), false);
    await flush(() => byText('Siguiente').click());
    assert.equal(new URL(requests.at(-1), 'http://localhost').searchParams.get('page'), '2');
    await flush(async () => { value('[aria-label="Buscar archivos"]', 'FILE-ABC'); await new Promise((resolve) => setTimeout(resolve, 280)); });
    query = new URL(requests.at(-1), 'http://localhost').searchParams;
    assert.equal(query.get('search'), 'FILE-ABC'); assert.equal(query.get('page'), '1');
  } finally { await close(); }
});

test('A new installation explains public upload requirements and allows private upload without projects', { timeout: 15000 }, async () => {
  reset(); const get = api.get;
  api.get = async (url) => url.startsWith('/api/projects?') ? { items: [] } : get(url);
  const close = await mount();
  try {
    assert.equal(byText('Subir a carpeta pública').disabled, true);
    assert.match(document.querySelector('#folder-upload-hint').textContent, /No tienes proyectos disponibles/);
    assert.equal(byText('Subir a carpeta pública').getAttribute('aria-describedby'), 'folder-upload-hint');
    assert.equal(document.querySelector('input[type="file"]').disabled, false);
    await flush(() => choose(new window.File(['Notas'], 'notas.txt', { type: 'text/plain' })));
    assert.equal(byText('Subir a carpeta pública').disabled, true);
    await flush(() => byText('Privada').click());
    assert.equal(byText('Subir a carpeta privada').disabled, false);
    assert.equal(document.querySelector('#folder-upload-hint'), null);
    await flush(() => byText('Subir a carpeta privada').click());
    assert.equal(uploads.length, 1);
    assert.equal(uploads[0].body.get('visibility'), 'Private');
    assert.equal(uploads[0].body.has('projectId'), false);
    assert.equal(byText('Subir a carpeta privada').disabled, true);
    assert.match(document.querySelector('#folder-upload-hint').textContent, /Selecciona un archivo/);
  } finally { await close(); }
});

test('Failed project loading explains how to retry while private selection still enables upload', { timeout: 15000 }, async () => {
  reset(); const get = api.get;
  api.get = async (url) => { if (url.startsWith('/api/projects?')) throw new Error('Sin conexión'); return get(url); };
  const close = await mount();
  try {
    assert.match(document.querySelector('#folder-upload-hint').textContent, /Reintenta cargar los proyectos/);
    await flush(() => byText('Privada').click());
    assert.match(document.querySelector('#folder-upload-hint').textContent, /Selecciona un archivo/);
    assert.equal(byText('Subir a carpeta privada').disabled, true);
    await flush(() => choose(new window.File(['Notas'], 'notas.md', { type: 'text/markdown' })));
    assert.equal(byText('Subir a carpeta privada').disabled, false);
    await flush(() => byText('Pública').click());
    assert.equal(byText('Subir a carpeta pública').disabled, true);
    api.get = get;
    await flush(() => byText('Reintentar proyectos').click());
    assert.equal(byText('Subir a carpeta pública').disabled, false);
    assert.equal(document.querySelector('#folder-upload-hint'), null);
  } finally { await close(); }
});

test('Chooser rejects SVG/oversize/empty files, uploads multipart and preserves selection after failure', { timeout: 15000 }, async () => {
  reset(); const close = await mount();
  try {
    await flush(() => choose(new window.File(['<svg/>'], 'unsafe.svg', { type: 'image/svg+xml' })));
    assert.match(document.querySelector('[role="alert"]').textContent, /tipo de archivo no está permitido/); assert.equal(uploads.length, 0);
    const large = new window.File(['x'], 'large.zip'); Object.defineProperty(large, 'size', { value: 50 * 1024 * 1024 + 1 });
    await flush(() => choose(large)); assert.match(document.querySelector('[role="alert"]').textContent, /50 MB/);
    await flush(() => choose(new window.File([], 'empty.txt'))); assert.match(document.querySelector('[role="alert"]').textContent, /vacío/);
    await flush(() => choose(new window.File(['Notas reales'], 'notas.txt', { type: 'text/plain' })));
    await flush(() => value('[aria-label="Descripción del archivo"]', 'Acuerdo del equipo'));
    failUpload = true;
    await flush(() => document.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.match(document.querySelector('[role="alert"]').textContent, /Carga interrumpida/);
    assert.match(document.querySelector('.folder-selected').textContent, /notas.txt/);
    failUpload = false;
    await flush(() => document.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    const body = uploads.at(-1).body;
    assert.equal(body.get('file').name, 'notas.txt'); assert.equal(body.get('visibility'), 'Public'); assert.equal(body.get('projectId'), 'p1'); assert.equal(body.get('description'), 'Acuerdo del equipo');
    assert.equal(document.querySelector('.folder-selected'), null);
    assert.match(document.querySelector('[role="status"]').textContent, /Archivo subido · FILE-ABC/);
  } finally { await close(); }
});

test('Private drop upload omits project, image previews/downloads use protected routes and only uploader can delete', { timeout: 15000 }, async () => {
  reset(); const close = await mount();
  try {
    await flush(() => byText('Privada').click());
    const drop = new window.Event('drop', { bubbles: true, cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [new window.File(['Private note'], 'privado.md', { type: 'text/markdown' })] } });
    await flush(() => document.querySelector('form').dispatchEvent(drop));
    await flush(() => document.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.equal(uploads.at(-1).body.get('visibility'), 'Private'); assert.equal(uploads.at(-1).body.has('projectId'), false);
    assert.equal(document.querySelector('img').getAttribute('src'), '/api/files/f2/preview');
    assert.equal(document.querySelectorAll('.folder-file-card button.danger').length, 1);
    assert.equal(document.querySelector('.folder-file-actions a').getAttribute('href'), '/api/files/f1/download');
    await flush(() => document.querySelector('[aria-label="Ver imagen Captura.png"]').click());
    assert.equal(document.querySelector('[role="dialog"] img').getAttribute('src'), '/api/files/f2/preview');
    await flush(() => document.querySelector('[aria-label="Cerrar imagen"]').click());
    failDelete = true; await flush(() => byText('Eliminar').click());
    assert.match(document.querySelector('[role="alert"]').textContent, /No se pudo eliminar/); assert.deepEqual(deletes, ['/api/files/f1']);
  } finally { await close(); }
});

test('List errors are retryable and delayed public responses cannot replace private results', { timeout: 15000 }, async () => {
  reset(); failList = true; const close = await mount();
  try {
    assert.match(document.querySelector('[role="alert"]').textContent, /Sin conexión/);
    failList = false; await flush(() => byText('Reintentar carpeta').click()); assert(document.querySelector('.folder-file-card'));
    const get = api.get; let resolvePublic;
    api.get = (url) => url.includes('visibility=Public') ? new Promise((resolve) => { resolvePublic = resolve; }) : get(url);
    await flush(() => byText('Actualizar').click());
    await flush(() => byText('Privada').click());
    await flush(() => resolvePublic({ items: [{ ...pdf, name: 'Respuesta pública antigua' }], total: 1, page: 1, pageSize: 50 }));
    assert.doesNotMatch(document.querySelector('#folder-content').textContent, /Respuesta pública antigua/);
    assert.equal(document.querySelector('[role="tab"][aria-selected="true"]').textContent, 'Privada');
  } finally { await close(); }
});

test('Multipart API preserves browser boundary and sends session credentials plus antiforgery', async () => {
  let captured;
  global.fetch = async (url, init) => url.includes('antiforgery') ? { ok: true, json: async () => ({ token: 'test-token' }) } : (captured = init, { ok: true, status: 200, text: async () => JSON.stringify(pdf) });
  await apiModule.refreshCsrf(); const form = new FormData(); form.append('file', new window.File(['x'], 'x.txt'));
  const result = await realUpload('/api/files', form);
  assert.equal(result.code, pdf.code); assert.equal(captured.body, form); assert.equal(captured.credentials, 'same-origin');
  assert.equal(captured.headers['X-XSRF-TOKEN'], 'test-token'); assert.equal(captured.headers['Content-Type'], undefined, 'browser supplies multipart boundary');
});
