// Real Layout mobile-menu behavior in an isolated DOM (no server or account calls).
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
Object.defineProperty(window, 'innerWidth', { value: 390, writable: true });
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
const appPath = path.join(frontend, 'src/App.tsx');
require.cache[appPath] = { id: appPath, filename: appPath, loaded: true, exports: {
  useAuth: () => ({ user: { name: 'Test', organizationName: 'Test' }, refresh: async () => {} }),
} };
const { api } = require(path.join(frontend, 'src/api.ts'));
api.get = async () => ({ count: 0 });
const Layout = require(path.join(frontend, 'src/components/Layout.tsx')).default;
function App() {
  return React.createElement(NavigationGuardProvider, null,
    React.createElement(Routes, null,
      React.createElement(Route, { path: '/', element: React.createElement(Layout) },
        React.createElement(Route, { path: 'diagram', element: React.createElement('div', null, 'Diagram') }),
        React.createElement(Route, { path: 'projects', element: React.createElement('div', null, 'Projects') }))));
}
const tick = () => new Promise((resolve) => setImmediate(resolve));
async function flush(action) { await act(async () => { action?.(); await tick(); }); }
const toggle = () => document.querySelector('.mobile-menu-toggle');
const close = () => document.querySelector('.mobile-sidebar-close');
const key = (name, shiftKey = false) => document.activeElement.dispatchEvent(new window.KeyboardEvent('keydown', { key: name, shiftKey, bubbles: true, cancelable: true }));
test('Mobile navigation manages focus and closes after route changes', { timeout: 15000 }, async () => {
  const router = createMemoryRouter([{ path: '*', element: React.createElement(App) }], { initialEntries: ['/diagram'] });
  const root = createRoot(document.getElementById('root'));
  await flush(() => root.render(React.createElement(RouterProvider, { router })));
  assert.equal(toggle().getAttribute('aria-controls'), 'app-navigation');
  assert.equal(toggle().getAttribute('aria-expanded'), 'false');
  await flush(() => toggle().click());
  assert.equal(toggle().getAttribute('aria-expanded'), 'true');
  assert(document.getElementById('app-navigation').classList.contains('mobile-open'));
  assert.equal(document.activeElement, close(), 'opening puts focus inside the mobile navigation');
  await flush(() => key('Tab', true));
  assert.equal(document.activeElement.getAttribute('aria-label'), 'Cerrar sesión', 'Shift+Tab stays in the navigation');
  await flush(() => key('Tab'));
  assert.equal(document.activeElement, close(), 'Tab wraps to first control');
  await flush(() => key('Escape'));
  assert.equal(toggle().getAttribute('aria-expanded'), 'false');
  assert.equal(document.activeElement, toggle(), 'Escape returns focus to opener');
  await flush(() => toggle().click());
  await flush(() => document.querySelector('.sidebar-backdrop').click());
  assert.equal(toggle().getAttribute('aria-expanded'), 'false', 'backdrop closes navigation');
  await flush(() => toggle().click());
  await flush(() => document.querySelector('a[href="/projects"]').click());
  assert.equal(router.state.location.pathname, '/projects');
  assert.equal(toggle().getAttribute('aria-expanded'), 'false', 'successful navigation closes overlay');
  await flush(() => toggle().click());
  await flush(() => { void router.navigate(-1); });
  assert.equal(router.state.location.pathname, '/diagram');
  assert.equal(toggle().getAttribute('aria-expanded'), 'false', 'browser history closes overlay');
  await flush(() => toggle().click());
  await flush(() => { window.innerWidth = 1024; window.dispatchEvent(new window.Event('resize')); });
  assert.equal(toggle().getAttribute('aria-expanded'), 'false', 'desktop resize releases modal focus trap');
  await flush(() => root.unmount()); router.dispose();
  console.log('PASS: mobile menu aria state, opening focus, Tab trap, Escape/opener focus, backdrop, route/history closure and desktop resize.');
});
