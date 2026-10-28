// TaskEditorPanel with real React input events: dates feed the same WorkItem
// payload used by task detail and by calendar projections. No backend calls.
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
  const result = ts.transpileModule(fs.readFileSync(filename, 'utf8'), {
    fileName: filename,
    compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2020 },
  });
  module._compile(result.outputText, filename);
};
require.extensions['.css'] = () => {};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { MemoryRouter } = require(path.join(frontend, 'node_modules/react-router-dom'));
const { api } = require(path.join(frontend, 'src/api.ts'));
const TaskEditorPanel = require(path.join(frontend, 'src/components/TaskEditorPanel.tsx')).default;
const state = { id: 'state-1', name: 'Backlog', isDefault: true };
const tick = () => new Promise((resolve) => setImmediate(resolve));
async function flush(action) { await act(async () => { await action?.(); await tick(); }); }

test('Task editor sets, moves and clears dueDate in payload; failed save preserves selected date', { timeout: 15000 }, async () => {
  for (const scenario of [
    { initial: null, next: '2026-10-29', failFirst: true },
    { initial: '2026-10-29T00:00:00', next: '2026-10-30' },
    { initial: '2026-10-30T00:00:00', next: '' },
  ]) {
    const task = { id: 'task-1', title: 'Validar carrito', code: 'CMP-02', projectId: 'p1', type: 0, priority: 1, stateId: state.id, rowVersion: 'v1', dueDate: scenario.initial };
    const writes = [], saved = [];
    let fail = scenario.failFirst;
    api.get = async (url) => url.endsWith('/members') ? [] : { projectName: 'Test', tasks: [], permissions: [1, 2, 3, 4, 5], states: [state] };
    api.put = async (url, payload) => {
      writes.push({ url, payload });
      if (fail) { fail = false; throw new Error('Sin conexión'); }
      return { ...task, ...payload, rowVersion: 'v2' };
    };
    const container = document.createElement('div'); document.body.appendChild(container);
    const root = createRoot(container);
    try {
      await flush(() => root.render(React.createElement(MemoryRouter, null, React.createElement(TaskEditorPanel, {
        task, projectId: 'p1', states: [state], onClose: () => {}, onSaved: (result) => saved.push(result),
      }))));
      const input = container.querySelector('#task-due');
      assert.equal(input.value, scenario.initial?.slice(0, 10) || '');
      await flush(() => {
        // Using the native setter represents browser input rather than manually
        // advancing React's tracked value with element.value = ... .
        Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set.call(input, scenario.next);
        input.dispatchEvent(new window.Event('input', { bubbles: true }));
        input.dispatchEvent(new window.Event('change', { bubbles: true }));
      });
      assert.equal(input.value, scenario.next);
      const submit = () => container.querySelector('form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true }));
      await flush(submit);
      assert.equal(writes[0].url, '/api/workitems/task-1');
      assert.equal(writes[0].payload.dueDate, scenario.next || null);
      assert.equal(writes[0].payload.rowVersion, 'v1');
      if (scenario.failFirst) {
        assert.match(container.querySelector('[role="alert"]').textContent, /Sin conexión/);
        assert.equal(input.value, scenario.next);
        assert.equal(saved.length, 0);
        await flush(submit);
        assert.equal(writes[1].payload.dueDate, scenario.next);
      }
      assert.equal(saved.length, 1);
      assert.equal(saved[0].dueDate, scenario.next || null);
    } finally { await flush(() => root.unmount()); container.remove(); }
  }
});
