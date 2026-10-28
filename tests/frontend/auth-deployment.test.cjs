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
after(() => dom.window.close());
for (const ext of ['.ts', '.tsx']) require.extensions[ext] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), { fileName: filename, compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 } });
  module._compile(result.outputText, filename);
};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { createMemoryRouter, RouterProvider } = require(path.join(frontend, 'node_modules/react-router-dom'));
let refreshCalls = 0;
const appPath = path.join(frontend, 'src/App.tsx');
require.cache[appPath] = { id: appPath, filename: appPath, loaded: true, exports: { useAuth: () => ({ refresh: async () => { refreshCalls++; } }) } };
const { api } = require(path.join(frontend, 'src/api.ts'));
const LoginPage = require(path.join(frontend, 'src/pages/LoginPage.tsx')).default;
const RegisterPage = require(path.join(frontend, 'src/pages/RegisterPage.tsx')).default;
async function flush(action) { await act(async () => { await action?.(); await new Promise((resolve) => setImmediate(resolve)); }); }
function inputValue(input, value) { Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, value); input.dispatchEvent(new window.Event('input', { bubbles: true })); }
async function mount(route) {
  const container = document.createElement('div'); document.body.appendChild(container);
  const router = createMemoryRouter([{ path: '/login', element: React.createElement(LoginPage) }, { path: '/register', element: React.createElement(RegisterPage) }, { path: '/', element: React.createElement('div', { id: 'signed-in' }, 'Home') }], { initialEntries: [route] });
  const root = createRoot(container); await flush(() => root.render(React.createElement(React.StrictMode, null, React.createElement(RouterProvider, { router }))));
  return { router, close: async () => { await flush(() => root.unmount()); router.dispose(); container.remove(); } };
}

test('Closed registration exposes no form, does not POST synthetic submit, and login offers no public-registration link', async () => {
  const posts = []; refreshCalls = 0;
  api.get = async () => ({ enabled: false }); api.post = async (url) => { posts.push(url); };
  const screen = await mount('/register');
  try {
    assert.equal(document.querySelector('form'), null);
    assert.match(document.body.textContent, /registro público está cerrado/);
    await flush(() => document.querySelector('.auth-card').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.deepEqual(posts, []); assert.equal(refreshCalls, 0);
    await flush(() => { void screen.router.navigate('/login'); });
    assert.equal(document.querySelector('a[href="/register"]'), null);
    assert.match(document.body.textContent, /Solicita tu cuenta al administrador/);
    assert(document.querySelector('form'), 'closed registration does not disable existing-account login');
  } finally { await screen.close(); }
});

test('Unknown registration policy fails closed while loading or on lookup error', async () => {
  let rejectPolicy; const posts = []; refreshCalls = 0;
  api.get = () => new Promise((_, reject) => { rejectPolicy = reject; }); api.post = async (url) => { posts.push(url); };
  const screen = await mount('/register');
  try {
    assert.match(document.body.textContent, /Comprobando disponibilidad/); assert.equal(document.querySelector('form'), null);
    await flush(() => rejectPolicy(new Error('Network unavailable')));
    assert.match(document.body.textContent, /No se pudo comprobar/); assert.equal(document.querySelector('form'), null);
    assert.deepEqual(posts, []);
  } finally { await screen.close(); }
});

test('Open registration submits user fields but a server-side policy closure preserves them without authenticating', async () => {
  const posts = []; refreshCalls = 0;
  api.get = async () => ({ enabled: true });
  api.post = async (url, payload) => { posts.push({ url, payload }); throw Object.assign(new Error('El registro público está cerrado.'), { status: 403 }); };
  const screen = await mount('/register');
  try {
    const inputs = [...document.querySelectorAll('form input')];
    assert.equal(inputs.length, 4);
    await flush(() => ['Persona Test', 'persona@example.invalid', 'Secret12345', 'Equipo Test'].forEach((value, index) => inputValue(inputs[index], value)));
    await flush(() => document.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.equal(posts.length, 1); assert.equal(posts[0].url, '/api/account/register');
    assert.deepEqual(posts[0].payload, { fullName: 'Persona Test', email: 'persona@example.invalid', password: 'Secret12345', organizationName: 'Equipo Test' });
    assert.match(document.querySelector('.alert.error').textContent, /registro público está cerrado/);
    assert.equal(inputs[0].value, 'Persona Test'); assert.equal(inputs[1].value, 'persona@example.invalid');
    assert.equal(refreshCalls, 0); assert.equal(screen.router.state.location.pathname, '/register');
    await flush(() => { void screen.router.navigate('/login'); });
    assert(document.querySelector('a[href="/register"]'), 'login exposes registration only when policy explicitly enables it');
  } finally { await screen.close(); }
});
