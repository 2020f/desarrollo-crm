// Calendar integration UI: real page/router, controlled API, no server or SQL writes.
process.env.TZ = 'America/Santo_Domingo';
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
const ActualDate = Date;
class CalendarTestDate extends ActualDate {
  constructor(...args) { super(...(args.length ? args : ['2026-10-27T16:00:00Z'])); }
  static now() { return ActualDate.parse('2026-10-27T16:00:00Z'); }
}
global.Date = CalendarTestDate;
after(() => { global.Date = ActualDate; dom.window.close(); });
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
const { api } = require(path.join(frontend, 'src/api.ts'));
const { default: CalendarPage, parseCalendarDate } = require(path.join(frontend, 'src/pages/CalendarPage.tsx'));
const due = {
  id: 'derived-task', title: 'Corregir total', type: 0, isAllDay: true,
  start: '2026-10-27T00:00:00', end: '2026-10-28T00:00:00',
  sourceType: 'WorkItem', sourceId: 'task-1', dateKind: 'Due', sourceRoute: '/tasks/task-1',
  isDerived: true, canEdit: false, canDelete: false, projectId: 'p1', priority: 2,
};
const period = { ...due, id: 'derived-project', title: 'Periodo del proyecto', sourceType: 'Project', sourceId: 'p1', dateKind: 'Period', sourceRoute: '/projects/p1', start: '2026-10-26T00:00:00', end: '2026-10-29T00:00:00' };
const manual = { ...due, id: 'manual-1', title: 'Reserva del equipo', type: 1, sourceType: 'CalendarEvent', sourceId: 'manual-1', dateKind: null, sourceRoute: '/calendar', isDerived: false, canEdit: true, canDelete: true, linkedWorkItemId: 'task-1' };
const meeting = { ...manual, id: 'meeting-1', title: 'Reunión con hora', isAllDay: false, start: '2026-10-27T05:00:00Z', end: '2026-10-27T06:00:00Z' };
let resultEvents, failCalendar, failSave, failDelete, calendarRequests, writes;
function reset() {
  resultEvents = [due, period, manual, meeting]; failCalendar = false; failSave = false; failDelete = false;
  calendarRequests = []; writes = [];
  api.get = async (url) => {
    if (url.startsWith('/api/projects?')) return { items: [{ id: 'p1', name: 'Proyecto uno' }], total: 1, page: 1, pageSize: 200 };
    if (url.includes('/members')) return [];
    if (url.startsWith('/api/calendar?')) { calendarRequests.push(url); if (failCalendar) throw new Error('Red interrumpida'); return resultEvents; }
    throw new Error('Unexpected GET ' + url);
  };
  api.put = api.post = async (url, payload) => { writes.push({ url, payload }); if (failSave) throw new Error('No se pudo guardar'); const saved = { ...manual, ...payload }; resultEvents = [saved]; return saved; };
  api.del = async (url) => { writes.push({ url, method: 'DELETE' }); if (failDelete) throw new Error('Sin conexión'); };
  window.confirm = () => true;
}
const tick = () => new Promise((resolve) => setImmediate(resolve));
async function flush(action) { await act(async () => { await action?.(); await tick(); }); }
async function mount() {
  const container = document.createElement('div'); document.body.appendChild(container);
  const router = createMemoryRouter([
    { path: '/calendar', element: React.createElement(CalendarPage) },
    { path: '/tasks/:id', element: React.createElement('div', { id: 'task-origin' }, 'Task detail') },
  ], { initialEntries: ['/calendar'] });
  const root = createRoot(container);
  await flush(() => root.render(React.createElement(RouterProvider, { router })));
  return { router, close: async () => { await flush(() => root.unmount()); router.dispose(); container.remove(); } };
}
function byText(text) { return [...document.querySelectorAll('button')].find((button) => button.textContent.trim() === text); }
function eventButton(title) { return [...document.querySelectorAll('button[aria-label]')].find((button) => button.getAttribute('aria-label').startsWith(title + ' ·')); }
function setValue(selector, value) {
  const element = document.querySelector(selector);
  const prototype = element instanceof window.HTMLSelectElement ? window.HTMLSelectElement.prototype : window.HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(prototype, 'value').set.call(element, value);
  element.dispatchEvent(new window.Event(element instanceof window.HTMLSelectElement ? 'change' : 'input', { bubbles: true }));
}

test('Civil dates and source labels appear in all four views; derived opens its original record', { timeout: 15000 }, async () => {
  reset();
  assert.equal(parseCalendarDate('2026-10-27T00:00:00Z').getDate(), 27, 'civil day never shifts to October 26 in UTC-4');
  const screen = await mount();
  try {
    const query = new URL(calendarRequests[0], 'http://localhost').searchParams;
    assert.equal(query.get('timeZoneId'), 'America/Santo_Domingo');
    assert.match(query.get('start'), /Z$/); assert.match(query.get('end'), /Z$/);
    assert.equal(query.get('start'), '2026-10-01T04:00:00.000Z', 'query starts at local midnight expressed as UTC');
    assert.match(document.querySelector('[data-calendar-date="2026-10-27"]').textContent, /Corregir total/);
    assert.doesNotMatch(document.querySelector('[data-calendar-date="2026-10-26"]').textContent, /Corregir total/);
    assert.doesNotMatch(document.querySelector('[data-calendar-date="2026-10-28"]').textContent, /Corregir total/, 'exclusive end is not displayed as another day');
    assert.match(eventButton(due.title).textContent, /Tarea · Vencimiento/);
    for (const view of ['Semana', 'Día']) {
      await flush(() => byText(view).click());
      assert.match(document.querySelector('.calendar-allday [data-calendar-date="2026-10-27"]').textContent, /Corregir total/);
      assert.match(document.querySelector('.calendar-allday').textContent, /Proyecto · Periodo/);
      assert(eventButton(meeting.title), 'events before the previous 06:00 cutoff remain visible');
      assert.match(eventButton(due.title).getAttribute('aria-label'), /Todo el día/);
    }
    await flush(() => byText('Agenda').click());
    assert.match(document.querySelector('.calendar-agenda').textContent, /Tarea · Vencimiento/);
    assert.match(document.querySelector('.calendar-agenda').textContent, /Todo el día/);
    await flush(() => eventButton(due.title).click());
    assert.equal(screen.router.state.location.pathname, '/tasks/task-1');
    assert(document.getElementById('task-origin'));
    assert.equal(document.querySelector('[role="dialog"]'), null, 'derived dates never open an event editor');
    assert.equal(writes.length, 0, 'opening derived source cannot PUT or DELETE a fabricated event');
  } finally { await screen.close(); }
});

test('All-day manual editor shows inclusive date inputs and sends civil exclusive bounds while retaining links', { timeout: 15000 }, async () => {
  reset(); const screen = await mount();
  try {
    await flush(() => eventButton(manual.title).click());
    assert.equal(document.querySelector('#calendar-event-start').type, 'date');
    assert.equal(document.querySelector('#calendar-event-start').value, '2026-10-27');
    assert.equal(document.querySelector('#calendar-event-end').value, '2026-10-27', 'one-day event does not expose exclusive next day as final date');
    failSave = true;
    await flush(() => document.querySelector('.calendar-event-modal form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.match(document.querySelector('.calendar-event-modal [role="alert"]').textContent, /No se pudo guardar/);
    assert.equal(document.querySelector('#calendar-event-start').value, '2026-10-27', 'failed save retains draft');
    failSave = false;
    await flush(() => document.querySelector('.calendar-event-modal form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    const payload = writes.at(-1).payload;
    assert.equal(payload.start, '2026-10-27T00:00:00'); assert.equal(payload.end, '2026-10-28T00:00:00');
    assert.equal(payload.timeZoneId, 'America/Santo_Domingo'); assert.equal(payload.linkedWorkItemId, 'task-1');
    assert.equal(document.querySelector('[role="dialog"]'), null);
    await flush(() => eventButton(manual.title).click());
    await flush(() => setValue('#calendar-event-start', '2027-01-03'));
    await flush(() => setValue('#calendar-event-end', '2027-01-03'));
    await flush(() => document.querySelector('.calendar-event-modal form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.match(document.querySelector('.calendar-toolbar h1').textContent, /Enero 2027/, 'saving a moved date focuses its new period');
    assert.match(document.querySelector('[data-calendar-date="2027-01-03"]').textContent, /Reserva del equipo/);
  } finally { await screen.close(); }
});

test('Calendar errors are visible and retryable; focus refreshes source dates and filters keep origins separate', { timeout: 15000 }, async () => {
  reset();
  const get = api.get;
  let rejectLoad;
  api.get = (url) => url.startsWith('/api/calendar?') ? new Promise((_, reject) => { rejectLoad = reject; }) : get(url);
  const screen = await mount();
  try {
    assert.equal(document.querySelector('.calendar-page').getAttribute('aria-busy'), 'true');
    assert.match(document.querySelector('[role="status"]').textContent, /Cargando calendario/);
    await flush(() => rejectLoad(new Error('Red interrumpida')));
    assert.match(document.querySelector('[role="alert"]').textContent, /No se pudo cargar el calendario/);
    api.get = get;
    await flush(() => byText('Reintentar calendario').click());
    assert(eventButton(due.title)); assert.equal(document.querySelector('[role="alert"]'), null);
    await flush(() => setValue('[aria-label="Origen del calendario"]', 'WorkItem'));
    assert(eventButton(due.title)); assert.equal(eventButton(manual.title), undefined);
    await flush(() => setValue('[aria-label="Origen del calendario"]', ''));
    resultEvents = [{ ...due, title: 'Fecha actualizada desde la tarea' }];
    await flush(async () => { window.dispatchEvent(new window.Event('focus')); await new Promise((resolve) => setTimeout(resolve, 150)); });
    assert(eventButton('Fecha actualizada desde la tarea'), 'returning focus refreshes edits made in original records');
  } finally { await screen.close(); }
});

test('Timed manual edits use UTC, all-day toggle has valid dates, and delete errors retain the editor', { timeout: 15000 }, async () => {
  reset(); const screen = await mount();
  try {
    await flush(() => eventButton(meeting.title).click());
    assert.equal(document.querySelector('#calendar-event-start').value, '2026-10-27T01:00');
    await flush(() => document.querySelector('.calendar-event-modal form').dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })));
    assert.equal(writes.at(-1).payload.start, '2026-10-27T05:00:00.000Z');
    await flush(() => eventButton(meeting.title).click());
    await flush(() => document.querySelector('.calendar-allday-toggle input').click());
    assert.equal(document.querySelector('#calendar-event-start').value, '2026-10-27');
    assert.equal(document.querySelector('#calendar-event-end').value, '2026-10-27');
    failDelete = true;
    await flush(() => byText('Eliminar').click());
    assert.match(document.querySelector('.calendar-event-modal [role="alert"]').textContent, /No se pudo eliminar/);
    assert(document.querySelector('[role="dialog"]'));
  } finally { await screen.close(); }
});

test('Read-only manual event shows details without edit or delete actions', { timeout: 15000 }, async () => {
  reset(); resultEvents = [{ ...manual, canEdit: false, canDelete: false }];
  const screen = await mount();
  try {
    await flush(() => eventButton(manual.title).click());
    assert.match(document.querySelector('#calendar-editor-title').textContent, /Detalle del evento/);
    assert.equal(document.querySelector('.calendar-event-fields').disabled, true);
    assert.equal(byText('Guardar'), undefined); assert.equal(byText('Eliminar'), undefined);
    assert.equal(document.activeElement.getAttribute('aria-label'), 'Cerrar evento');
    assert.equal(writes.length, 0);
  } finally { await screen.close(); }
});
