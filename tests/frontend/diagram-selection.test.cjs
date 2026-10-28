// TaskDiagramPage save/reload regression. Canvas rendering and the editor are
// adapters; page state, handlers, effects and controlled node data are real.
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
require.extensions['.css'] = () => {};
const React = require(path.join(frontend, 'node_modules/react'));
const { act } = React;
const { createRoot } = require(path.join(frontend, 'node_modules/react-dom/client'));
const { createMemoryRouter, RouterProvider } = require(path.join(frontend, 'node_modules/react-router-dom'));
const { NavigationGuardProvider } = require(path.join(frontend, 'src/components/NavigationGuard.tsx'));
const task = (id, title, parentTaskId, x) => ({
  id, code: id, title, displayTitle: title, parentTaskId, x, y: 0, depth: parentTaskId ? 1 : 0,
  stateId: 'backlog', stateName: 'Backlog', priority: 1, accentColor: '#9B8AFB',
  rowVersion: 'v1', positionRowVersion: 'p1', canEdit: true, canCreateChild: true,
});
const parent = task('CMP-02', 'Carrito', undefined, 0);
const child = task('CMP-06', 'Validar disponibilidad', parent.id, 440);
let diagram = { tasks: [parent], viewportX: 0, viewportY: 0, zoom: 1, projectName: 'Test', permissions: [1, 2, 3], states: [], collapsedTaskIds: [] };
let reload, delayReload = false, flowProps, centers = [];
const rf = {
  getNode: (id) => { const node = flowProps?.nodes.find((item) => item.id === id); return node && { ...node, measured: { width: 300, height: 400 } }; },
  getZoom: () => 1,
  setCenter: async (x, y) => { centers.push({ x, y }); },
  setViewport: async () => {}, fitView: async () => {}, zoomIn: async () => {}, zoomOut: async () => {},
};
function Flow(props) {
  flowProps = props;
  return React.createElement('div', { id: 'flow' }, props.nodes.map((node) =>
    React.createElement('div', { key: node.id, 'data-id': node.id, className: 'react-flow__node' + (node.selected ? ' selected' : ''),
      onClick: () => props.onNodeClick?.({}, node) },
    React.createElement('button', { 'data-child-for': node.id, onClick: () => node.data.onCreateChild(node.data.task) }, 'Create child'))));
}
const flowPath = require.resolve(path.join(frontend, 'node_modules/@xyflow/react'));
require.cache[flowPath] = { id: flowPath, filename: flowPath, loaded: true, exports: {
  ReactFlow: Flow, ReactFlowProvider: ({ children }) => children, Background: () => null, BackgroundVariant: { Dots: 'dots' },
  useReactFlow: () => rf, useViewport: () => ({ zoom: 1 }), useNodesInitialized: () => true,
  applyNodeChanges: (changes, nodes) => nodes.map((node) => changes.reduce((current, change) => current.id === change.id && change.type === 'select' ? { ...current, selected: change.selected } : current, node)),
  Handle: () => null, Position: { Left: 'left', Right: 'right' },
} };
const editorPath = path.join(frontend, 'src/components/TaskEditorPanel.tsx');
require.cache[editorPath] = { id: editorPath, filename: editorPath, loaded: true, exports: { __esModule: true,
  default: ({ onSaved }) => React.createElement('button', { id: 'save-child', onClick: () => onSaved({ ...child, projectId: 'p' }) }, 'Save child') } };
const { api } = require(path.join(frontend, 'src/api.ts'));
api.get = async () => delayReload ? new Promise((resolve) => { reload = resolve; }) : diagram;
api.patch = async (_, payload) => ({ positions: payload.positions });
api.put = async () => ({});
const Page = require(path.join(frontend, 'src/pages/TaskDiagramPage.tsx')).default;
const tick = () => new Promise((resolve) => setImmediate(resolve));
async function flush(action) { await act(async () => { action?.(); await tick(); }); }
test('Saved child remains selected and centered after delayed reload', { timeout: 15000 }, async () => {
  const router = createMemoryRouter([{ path: '/projects/:id/diagram', element: React.createElement(NavigationGuardProvider, null, React.createElement(Page)) }], { initialEntries: ['/projects/p/diagram'] });
  const root = createRoot(document.getElementById('root'));
  await flush(() => root.render(React.createElement(RouterProvider, { router })));
  await flush();
  await flush(() => document.querySelector('[data-child-for="CMP-02"]').click());
  assert.match(document.querySelector('.diagram-selection').textContent, /CMP-02/);
  delayReload = true;
  await flush(() => document.getElementById('save-child').click());
  // A delayed reload still contains the former selected node in the canvas.
  await flush(() => {
    flowProps.onSelectionChange?.({ nodes: [{ id: parent.id }] });
    flowProps.onNodesChange([{ id: parent.id, type: 'select', selected: true }]);
  });
  diagram = { ...diagram, tasks: [parent, child] };
  await flush(() => reload(diagram));
  await flush();
  assert.deepEqual(flowProps.nodes.filter((node) => node.selected).map((node) => node.id), [child.id], 'new child stays selected after delayed reload');
  assert.match(document.querySelector('.diagram-selection').textContent, /CMP-06/);
  assert(centers.some((center) => center.x === 590), 'new child is centered after it appears');
  await flush(() => flowProps.onNodesChange([{ id: child.id, type: 'select', selected: false }]));
  assert.equal(document.querySelector('.react-flow__node.selected')?.dataset.id, child.id, 'stale deselection cannot erase programmatic selection');
  await flush(() => root.unmount()); router.dispose();
  console.log('PASS: saved child remains selected and centered across delayed reload and stale selection/deselection updates.');
});
