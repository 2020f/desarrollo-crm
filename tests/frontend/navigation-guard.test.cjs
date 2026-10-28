// Isolated regression harness; no server, database or account calls.
// Run from the repository root: npm --prefix src/Desarrollo.Web/ClientApp run test:diagram
const assert = require('node:assert/strict');
const { test, after } = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const frontend = path.resolve(__dirname, '../../src/Desarrollo.Web/ClientApp');
const { JSDOM } = require(path.join(frontend, 'node_modules/jsdom'));
const ts = require(path.join(frontend, 'node_modules/typescript'));
const dom = new JSDOM('<!doctype html><html><body><div id="root"></div></body></html>', { url: 'http://localhost/' });
global.window = dom.window;
global.document = dom.window.document;
Object.defineProperty(global, 'navigator', { value: dom.window.navigator, configurable: true });
global.IS_REACT_ACT_ENVIRONMENT = true;
after(() => dom.window.close());
require.extensions['.tsx'] = (module, filename) => {
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 },
  });
  module._compile(result.outputText, filename);
};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { createMemoryRouter, RouterProvider, Routes, Route } = require(path.join(frontend, 'node_modules/react-router-dom'));
const { NavigationGuardProvider, useNavigationGuard } = require(path.join(frontend, 'src/components/NavigationGuard.tsx'));

let pending = false, calls = 0, externalActions = 0, guardActions;
let saver = async () => { pending = false; return true; };
function Diagram() {
  const { register, saveBeforeLeaving } = useNavigationGuard();
  guardActions = saveBeforeLeaving;
  React.useEffect(() => register({ hasPendingChanges: () => pending, saveBeforeLeaving: () => { calls++; return saver(); } }), [register]);
  return React.createElement('div', { id: 'diagram' }, 'Draft stays mounted');
}
function App() {
  return React.createElement(NavigationGuardProvider, null,
    React.createElement(Routes, null,
      React.createElement(Route, { path: '/diagram', element: React.createElement(Diagram) }),
      React.createElement(Route, { path: '*', element: React.createElement('div', { id: 'other' }, 'Other screen') })));
}
const tick = () => new Promise((resolve) => setImmediate(resolve));
async function flush(action) { await act(async () => { action?.(); await tick(); }); }

test('Pending diagram changes block navigation until saving succeeds', { timeout: 15000 }, async () => {
  const router = createMemoryRouter([{ path: '*', element: React.createElement(App) }], {
    initialEntries: ['/earlier', '/diagram'], initialIndex: 1,
  });
  const root = createRoot(document.getElementById('root'));
  await flush(() => root.render(React.createElement(React.StrictMode, null, React.createElement(RouterProvider, { router }))));
  assert(document.getElementById('diagram'));

  let finish;
  pending = true;
  saver = () => new Promise((resolve) => { finish = resolve; });
  await flush(() => { void router.navigate('/target'); });
  assert.equal(router.state.location.pathname, '/diagram', 'push waits for the save');
  assert(document.getElementById('diagram'), 'draft remains mounted during save');
  await flush(() => finish(false));
  assert.equal(router.state.location.pathname, '/diagram', 'failed save cancels push');
  assert(document.getElementById('diagram'), 'failed save preserves draft');

  saver = async () => { pending = false; return true; };
  await flush(() => { void router.navigate('/target'); });
  await flush();
  assert.equal(router.state.location.pathname, '/target', 'successful save allows push');
  const savedCalls = calls;
  await flush(() => { void router.navigate(-1); });
  await flush();
  assert.equal(router.state.location.pathname, '/diagram');
  assert.equal(calls, savedCalls, 'clean history navigation does not write');

  pending = true;
  saver = async () => false;
  await flush(() => { void router.navigate(-1); });
  await flush();
  assert.equal(router.state.location.pathname, '/diagram', 'failed save cancels browser back');
  await flush(() => { void router.navigate(1); });
  await flush();
  assert.equal(router.state.location.pathname, '/diagram', 'failed save cancels browser forward');

  const unload = new window.Event('beforeunload', { cancelable: true });
  window.dispatchEvent(unload);
  assert.equal(unload.defaultPrevented, true, 'pending camera/position state protects document unload');

  await flush(() => { void guardActions().then((saved) => { if (saved) externalActions++; }); });
  assert.equal(externalActions, 0, 'logout/other external action is withheld on save failure');
  saver = async () => { pending = false; return true; };
  await flush(() => { void guardActions().then((saved) => { if (saved) externalActions++; }); });
  assert.equal(externalActions, 1, 'external action continues after successful save');
  const cleanUnload = new window.Event('beforeunload', { cancelable: true });
  window.dispatchEvent(cleanUnload);
  assert.equal(cleanUnload.defaultPrevented, false, 'clean screen leaves without an unload prompt');

  pending = true;
  saver = async () => { throw new Error('Network disconnected'); };
  await flush(() => { void router.navigate('/error-target'); });
  await flush();
  assert.equal(router.state.location.pathname, '/diagram', 'unexpected network rejection also preserves current screen');
  await flush(() => root.unmount());
  router.dispose();
  console.log('PASS: pending push, failed push, retained draft, successful retry, clean history, failed back/forward, pending/clean unload, guarded logout, rejected save.');
});
