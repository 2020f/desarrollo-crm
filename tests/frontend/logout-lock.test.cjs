// Real Layout + navigation guard in an isolated DOM; API/auth are controlled promises.
const assert = require('node:assert/strict');
const { test, after } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const frontend = path.resolve(__dirname, '../../src/Desarrollo.Web/ClientApp');
const { JSDOM } = require(path.join(frontend, 'node_modules/jsdom'));
const ts = require(path.join(frontend, 'node_modules/typescript'));
const dom = new JSDOM('<!doctype html><body><div id="root"></div></body>', { url: 'http://localhost/' });
global.window = dom.window; global.document = dom.window.document;
Object.defineProperty(global, 'navigator', { value: dom.window.navigator, configurable: true });
global.IS_REACT_ACT_ENVIRONMENT = true;
after(() => dom.window.close());
for (const ext of ['.ts', '.tsx']) require.extensions[ext] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    fileName: filename,
    compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 },
  });
  module._compile(result.outputText, filename);
};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { createMemoryRouter, RouterProvider, Routes, Route } = require(path.join(frontend, 'node_modules/react-router-dom'));
const { NavigationGuardProvider } = require(path.join(frontend, 'src/components/NavigationGuard.tsx'));
let finishPost, rejectPost, finishRefresh, refreshCalls = 0, postCalls = 0;
const auth = {
  user: { name: 'Test', organizationName: 'Test' },
  refresh: () => { refreshCalls++; return new Promise((resolve) => { finishRefresh = resolve; }); },
};
const appPath = path.join(frontend, 'src/App.tsx');
require.cache[appPath] = { id: appPath, filename: appPath, loaded: true, exports: { useAuth: () => auth } };
const { api } = require(path.join(frontend, 'src/api.ts'));
api.get = async () => ({ count: 0 });
api.post = () => { postCalls++; return new Promise((resolve, reject) => { finishPost = resolve; rejectPost = reject; }); };
const Layout = require(path.join(frontend, 'src/components/Layout.tsx')).default;
function App() {
  return React.createElement(NavigationGuardProvider, null,
    React.createElement(Routes, null,
      React.createElement(Route, { path: '/', element: React.createElement(Layout) },
        React.createElement(Route, { path: 'diagram', element: React.createElement('div', { id: 'diagram' }, 'Canvas controls') })),
      React.createElement(Route, { path: '/login', element: React.createElement('div', { id: 'login' }, 'Login') }),
      React.createElement(Route, { path: '*', element: React.createElement('div', { id: 'target' }, 'Other') })));
}
const tick = () => new Promise((resolve) => setImmediate(resolve));
async function flush(action) { await act(async () => { action?.(); await tick(); }); }
const isLocked = () => !!document.querySelector('[inert][aria-busy="true"]');
test('Logout keeps navigation locked through request and refresh', { timeout: 15000 }, async () => {
  const router = createMemoryRouter([{ path: '*', element: React.createElement(App) }], {
    initialEntries: ['/earlier', '/diagram', '/later'], initialIndex: 1,
  });
  const root = createRoot(document.getElementById('root'));
  await flush(() => root.render(React.createElement(RouterProvider, { router })));
  await flush(() => document.querySelector('[aria-label="Cerrar sesión"]').click());
  assert.equal(postCalls, 1);
  assert(isLocked(), 'shell remains inert after saves, while logout POST waits');
  assert(document.querySelector('[role="status"]'), 'logout lock is visible and announced');
  await flush(() => { void router.navigate(-1); });
  assert.equal(router.state.location.pathname, '/diagram', 'back is cancelled while logout waits');
  await flush(() => { void router.navigate(1); });
  assert.equal(router.state.location.pathname, '/diagram', 'forward is cancelled while logout waits');
  await flush(() => { void router.navigate('/competing-target'); });
  assert.equal(router.state.location.pathname, '/diagram', 'programmatic route is cancelled while logout waits');
  await flush(() => finishPost({}));
  assert.equal(refreshCalls, 1);
  assert(isLocked(), 'shell stays inert during delayed session refresh');
  await flush(() => { void router.navigate('/another-target'); });
  assert.equal(router.state.location.pathname, '/diagram');
  await flush(() => finishRefresh());
  await flush();
  assert.equal(router.state.location.pathname, '/login', 'only login is opened after logout, never a queued competing destination');

  await flush(() => { void router.navigate('/diagram'); });
  await flush(() => document.querySelector('[aria-label="Cerrar sesión"]').click());
  assert(isLocked());
  await flush(() => rejectPost(new Error('Network disconnected')));
  assert.equal(router.state.location.pathname, '/diagram', 'failed logout keeps current screen');
  assert.equal(isLocked(), false, 'failure releases input lock for retry');
  assert.match(document.querySelector('[role="alert"]').textContent, /No se pudo cerrar la sesión/);
  await flush(() => { void router.navigate('/after-failure'); });
  assert.equal(router.state.location.pathname, '/after-failure', 'failure releases navigation lock');
  await flush(() => root.unmount()); router.dispose();
  console.log('PASS: delayed logout keeps shell inert through POST and refresh; back/forward/programmatic routes are cancelled; only login follows success; failure preserves screen and releases locks.');
});
