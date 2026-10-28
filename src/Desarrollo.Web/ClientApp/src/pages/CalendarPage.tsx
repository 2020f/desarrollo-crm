import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api, qs } from '../api';
import type { CalendarEventDto, MemberDto, Paged, ProjectDto } from '../types';
import { PRIORITY_LABELS, initials } from '../constants';
import './calendar.css';

type ViewMode = 'month' | 'week' | 'day' | 'agenda';
type EditorState = { event?: CalendarEventDto; start: Date; end: Date };
const EVENT_COLORS = ['#9B8AFB', '#57D6C7', '#EAC385', '#E0788A', '#7C9CF5', '#4FB3D9', '#B48CF2', '#5FC48A'];
const TYPE_LABELS: Record<number, string> = { 0: 'Tarea', 1: 'Reunión', 2: 'Hito', 3: 'Publicación' };
const TYPE_DEFAULT_COLOR: Record<number, string> = { 0: '#9B8AFB', 1: '#57D6C7', 2: '#EAC385', 3: '#5FC48A' };
const SOURCE_LABELS: Record<string, string> = { CalendarEvent: 'Evento', WorkItem: 'Tarea', Project: 'Proyecto', Epic: 'Épica', Cycle: 'Ciclo', Milestone: 'Hito', Release: 'Entrega' };
const DATE_LABELS: Record<string, string> = { Period: 'Periodo', Start: 'Inicio', Due: 'Vencimiento', Planned: 'Fecha prevista', Released: 'Publicación' };
const VIEW_LABELS: Record<ViewMode, string> = { month: 'Mes', week: 'Semana', day: 'Día', agenda: 'Agenda' };
const MONTHS = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];
const DAYS_SHORT = ['Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb', 'Dom'];
const DAYS_FULL = ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'];

function startOfDay(d: Date) { return new Date(d.getFullYear(), d.getMonth(), d.getDate()); }
function startOfWeek(d: Date) { const result = startOfDay(d); result.setDate(result.getDate() - (result.getDay() + 6) % 7); return result; }
function addDays(d: Date, count: number) { const result = new Date(d); result.setDate(result.getDate() + count); return result; }
function addMonths(d: Date, count: number) { return new Date(d.getFullYear(), d.getMonth() + count, 1); }
function sameDay(a: Date, b: Date) { return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate(); }
function dateKey(d: Date) { const pad = (n: number) => String(n).padStart(2, '0'); return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`; }
/** Civil dates have no UTC conversion: 2026-10-27 stays October 27 in every zone. */
export function parseCalendarDate(value: string) { const [year, month, day] = value.slice(0, 10).split('-').map(Number); return new Date(year, month - 1, day); }
function eventStart(event: CalendarEventDto) { return event.isAllDay ? parseCalendarDate(event.start) : new Date(event.start); }
function eventEnd(event: CalendarEventDto) { return event.isAllDay ? parseCalendarDate(event.end) : new Date(event.end); }
function fmtDate(date: Date) { return date.toLocaleDateString('es-DO', { day: 'numeric', month: 'short', year: 'numeric' }); }
function fmtTime(value: string) { return new Date(value).toLocaleTimeString('es-DO', { hour: '2-digit', minute: '2-digit' }); }
function eventMeta(event: CalendarEventDto) { return `${SOURCE_LABELS[event.sourceType || 'CalendarEvent'] || 'Registro'} · ${event.isDerived ? DATE_LABELS[event.dateKind || ''] || 'Fecha' : TYPE_LABELS[event.type] || 'Evento'}`; }
function occursOn(event: CalendarEventDto, day: Date) { return eventStart(event) < addDays(day, 1) && eventEnd(event) > day; }
function eventDates(event: CalendarEventDto) {
  const first = eventStart(event), last = event.isAllDay ? addDays(eventEnd(event), -1) : eventEnd(event);
  if (event.isAllDay) return `${fmtDate(first)}${sameDay(first, last) ? '' : ' — ' + fmtDate(last)} · Todo el día`;
  return `${fmtDate(first)} ${fmtTime(event.start)} — ${sameDay(first, last) ? '' : fmtDate(last) + ' '}${fmtTime(event.end)}`;
}
function eventLabel(event: CalendarEventDto) { return `${event.title} · ${eventMeta(event)} · ${eventDates(event)}${event.isDerived ? ' · Abrir registro de origen' : ''}`; }
function eventKey(event: CalendarEventDto) { return `${event.sourceType || 'CalendarEvent'}:${event.sourceId || event.id}:${event.dateKind || ''}:${event.id}`; }
function colorBg(color?: string | null) { return /^#[0-9a-f]{6}$/i.test(color || '') ? `${color}22` : 'var(--primary-soft)'; }
function rangeFor(view: ViewMode, cursor: Date) {
  if (view === 'week') { const start = startOfWeek(cursor); return { start, end: addDays(start, 7) }; }
  if (view === 'day') { const start = startOfDay(cursor); return { start, end: addDays(start, 1) }; }
  return { start: new Date(cursor.getFullYear(), cursor.getMonth(), 1), end: addMonths(cursor, view === 'agenda' ? 3 : 1) };
}

export default function CalendarPage() {
  const navigate = useNavigate();
  const [view, setView] = useState<ViewMode>('month');
  const [cursor, setCursor] = useState(() => startOfDay(new Date()));
  const [events, setEvents] = useState<CalendarEventDto[]>([]);
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [projectId, setProjectId] = useState('');
  const [source, setSource] = useState('');
  const [modal, setModal] = useState<EditorState | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [projectError, setProjectError] = useState('');
  const active = useRef(true);
  const requestSequence = useRef(0);
  const timeZoneId = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, []);
  const range = useMemo(() => rangeFor(view, cursor), [view, cursor]);
  const rangeStart = range.start.toISOString(), rangeEnd = range.end.toISOString();

  useEffect(() => { active.current = true; return () => { active.current = false; requestSequence.current++; }; }, []);
  const loadProjects = useCallback(async () => {
    try {
      const result = await api.get<Paged<ProjectDto>>('/api/projects?pageSize=200');
      if (active.current) { setProjects(result.items); setProjectError(''); }
    } catch (failure) { if (active.current) setProjectError('No se pudieron cargar los proyectos: ' + (failure as Error).message); }
  }, []);
  useEffect(() => { void loadProjects(); }, [loadProjects]);

  const load = useCallback(async () => {
    const sequence = ++requestSequence.current;
    setLoading(true);
    try {
      const result = await api.get<CalendarEventDto[]>('/api/calendar' + qs({ start: rangeStart, end: rangeEnd, timeZoneId }));
      if (active.current && sequence === requestSequence.current) { setEvents(result); setError(''); }
    } catch (failure) {
      if (active.current && sequence === requestSequence.current) { setEvents([]); setError('No se pudo cargar el calendario: ' + (failure as Error).message); }
    } finally { if (active.current && sequence === requestSequence.current) setLoading(false); }
  }, [rangeStart, rangeEnd, timeZoneId]);
  useEffect(() => { setEvents([]); void load(); }, [load]);
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>;
    const refresh = () => { clearTimeout(timer); timer = setTimeout(() => void load(), 100); };
    const visible = () => { if (document.visibilityState === 'visible') refresh(); };
    window.addEventListener('focus', refresh); document.addEventListener('visibilitychange', visible);
    return () => { clearTimeout(timer); window.removeEventListener('focus', refresh); document.removeEventListener('visibilitychange', visible); };
  }, [load]);

  const move = (direction: number) => setCursor(view === 'week' ? addDays(cursor, direction * 7) : view === 'day' ? addDays(cursor, direction) : addMonths(cursor, direction));
  const title = view === 'day' ? `${DAYS_FULL[(cursor.getDay() + 6) % 7]} ${cursor.getDate()} de ${MONTHS[cursor.getMonth()]} ${cursor.getFullYear()}`
    : view === 'week' ? `${fmtDate(range.start)} — ${fmtDate(addDays(range.end, -1))}`
    : `${MONTHS[cursor.getMonth()]} ${cursor.getFullYear()}${view === 'agenda' ? ' — ' + MONTHS[addMonths(cursor, 2).getMonth()] + ' ' + addMonths(cursor, 2).getFullYear() : ''}`;
  const openCreate = (date: Date, hour = 9) => { const start = new Date(date); start.setHours(hour, 0, 0, 0); setModal({ start, end: new Date(start.getTime() + 3600000) }); };
  const openEvent = (event: CalendarEventDto) => {
    if (event.isDerived) {
      if (event.sourceRoute?.startsWith('/') && !event.sourceRoute.startsWith('//')) navigate(event.sourceRoute);
      else setError('No se encontró la ruta del registro de origen. Actualiza el calendario y vuelve a intentarlo.');
      return;
    }
    setModal({ event, start: eventStart(event), end: eventEnd(event) });
  };
  const saved = (event: CalendarEventDto) => {
    const target = eventStart(event);
    const nextRange = rangeFor(view, target);
    setModal(null); setCursor(startOfDay(target));
    if (nextRange.start.toISOString() === rangeStart && nextRange.end.toISOString() === rangeEnd) void load();
  };
  const visibleEvents = events.filter((event) => (!projectId || event.projectId === projectId) && (!source || (event.sourceType || 'CalendarEvent') === source));

  return <div className="calendar-page" aria-busy={loading}>
    <div className="calendar-toolbar row">
      <button className="btn" onClick={() => setCursor(startOfDay(new Date()))}>Hoy</button>
      <button className="btn" aria-label="Periodo anterior" onClick={() => move(-1)}>‹</button>
      <button className="btn" aria-label="Periodo siguiente" onClick={() => move(1)}>›</button>
      <h1 className="page-title">{title}</h1><div className="grow" />
      <button className="btn" onClick={() => void load()} disabled={loading}>Actualizar</button>
      <button className="btn primary" onClick={() => openCreate(cursor)}>+ Crear evento</button>
      <div className="view-switch" aria-label="Vista del calendario">{(Object.keys(VIEW_LABELS) as ViewMode[]).map((mode) =>
        <button key={mode} className={`view-btn ${view === mode ? 'active' : ''}`} aria-pressed={view === mode} onClick={() => setView(mode)}>{VIEW_LABELS[mode]}</button>)}</div>
    </div>
    <div className="calendar-filters">
      <label>Proyecto<select aria-label="Proyecto del calendario" className="input" value={projectId} onChange={(event) => setProjectId(event.target.value)}><option value="">Todos los proyectos</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <label>Origen<select aria-label="Origen del calendario" className="input" value={source} onChange={(event) => setSource(event.target.value)}><option value="">Todos los orígenes</option>{Object.entries(SOURCE_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      <span className="muted small">Zona horaria: {timeZoneId}. Las fechas de todo el día conservan su día.</span>
    </div>
    <p className="calendar-explanation muted small">Las fechas de tareas, proyectos y entregas se actualizan desde su registro de origen. Las reuniones y los demás eventos se gestionan aquí.</p>
    {projectError && <div className="alert error" role="alert">{projectError} <button className="btn sm" onClick={() => void loadProjects()}>Reintentar proyectos</button></div>}
    {error && <div className="alert error" role="alert">{error} <button className="btn sm" onClick={() => void load()}>Reintentar calendario</button></div>}
    {loading && <div className="muted small calendar-loading" role="status">Cargando calendario…</div>}
    {!loading && !error && visibleEvents.length === 0 && <div className="muted small calendar-loading" role="status">Sin fechas en este periodo{projectId || source ? ' con estos filtros' : ''}.</div>}
    {view === 'month' && <MonthView cursor={cursor} events={visibleEvents} onDayClick={openCreate} onEventClick={openEvent} onShowDay={(day) => { setCursor(day); setView('day'); }} />}
    {(view === 'week' || view === 'day') && <TimeGrid days={view === 'week' ? 7 : 1} anchor={view === 'week' ? startOfWeek(cursor) : startOfDay(cursor)} events={visibleEvents} onSlotClick={openCreate} onEventClick={openEvent} />}
    {view === 'agenda' && <AgendaView events={visibleEvents} onEventClick={openEvent} />}
    {modal && <EventModal initial={modal} projects={projects} timeZoneId={timeZoneId} onClose={() => setModal(null)} onSaved={saved} onDeleted={() => { setModal(null); void load(); }} />}
  </div>;
}

function MonthView({ cursor, events, onDayClick, onEventClick, onShowDay }: {
  cursor: Date; events: CalendarEventDto[]; onDayClick: (date: Date) => void; onEventClick: (event: CalendarEventDto) => void; onShowDay: (date: Date) => void;
}) {
  const first = new Date(cursor.getFullYear(), cursor.getMonth(), 1);
  const offset = (first.getDay() + 6) % 7;
  const count = new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0).getDate();
  const cells: (Date | null)[] = Array(offset).fill(null).concat(Array.from({ length: count }, (_, index) => new Date(cursor.getFullYear(), cursor.getMonth(), index + 1)));
  return <div className="cal calendar-month-scroll"><div className="calendar-month-grid">
    <div className="cal-grid cal-weekdays">{DAYS_SHORT.map((day) => <div key={day} className="cal-weekday">{day}</div>)}</div>
    <div className="cal-grid">{cells.map((day, index) => {
      if (!day) return <div key={'empty-' + index} className="cal-day cal-day-empty" />;
      const dayEvents = events.filter((event) => occursOn(event, day));
      const today = sameDay(day, new Date());
      return <div key={dateKey(day)} data-calendar-date={dateKey(day)} className={`cal-day ${today ? 'today' : ''}`} onClick={(event) => { if (event.target === event.currentTarget) onDayClick(day); }}>
        <button className={`cal-day-num ${today ? 'today-num' : ''}`} aria-label={'Ver ' + fmtDate(day)} onClick={() => onShowDay(day)}>{day.getDate()}</button>
        {dayEvents.slice(0, 4).map((event) => <EventChip key={eventKey(event)} event={event} onClick={() => onEventClick(event)} />)}
        {dayEvents.length > 4 && <button className="calendar-more" onClick={() => onShowDay(day)}>+{dayEvents.length - 4} más</button>}
      </div>;
    })}</div>
  </div></div>;
}

const HOUR_H = 56;
function TimeGrid({ days, anchor, events, onSlotClick, onEventClick }: {
  days: number; anchor: Date; events: CalendarEventDto[]; onSlotClick: (date: Date, hour: number) => void; onEventClick: (event: CalendarEventDto) => void;
}) {
  const columns = Array.from({ length: days }, (_, index) => addDays(anchor, index));
  return <div className="cal calendar-time-scroll"><div className={days === 7 ? 'calendar-week-grid' : 'calendar-day-grid'}>
    <div className="time-grid-head"><div className="time-gutter" />{columns.map((day) => <div key={dateKey(day)} className={`time-col-head ${sameDay(day, new Date()) ? 'today' : ''}`}><div className="small muted">{DAYS_SHORT[(day.getDay() + 6) % 7]}</div><div className={sameDay(day, new Date()) ? 'today-num' : ''}>{day.getDate()}</div></div>)}</div>
    <div className="calendar-allday" aria-label="Eventos de todo el día"><div className="time-gutter small muted">Todo el día</div>{columns.map((day) => <div key={dateKey(day)} className="calendar-allday-column" data-calendar-date={dateKey(day)}>{events.filter((event) => event.isAllDay && occursOn(event, day)).map((event) => <EventChip key={eventKey(event)} event={event} onClick={() => onEventClick(event)} />)}</div>)}</div>
    <div className="time-grid-scroll"><div className="time-grid" style={{ height: 24 * HOUR_H }}>
      {Array.from({ length: 24 }, (_, hour) => <div key={hour} className="hour-line" style={{ top: hour * HOUR_H }}><span className="hour-label">{hour}:00</span></div>)}
      <div className="time-cols" style={{ height: 24 * HOUR_H }}><div className="time-gutter" />{columns.map((day) => <div key={dateKey(day)} className="time-col" onClick={(event) => {
        if (event.target === event.currentTarget) { const rect = event.currentTarget.getBoundingClientRect(); onSlotClick(day, Math.max(0, Math.min(23, Math.floor((event.clientY - rect.top) / HOUR_H)))); }
      }}>{events.filter((event) => !event.isAllDay && occursOn(event, day)).map((event) => {
        const start = eventStart(event), end = eventEnd(event);
        const startMinute = start < day ? 0 : start.getHours() * 60 + start.getMinutes();
        const endMinute = end >= addDays(day, 1) ? 1440 : end.getHours() * 60 + end.getMinutes();
        return <button key={eventKey(event)} className="time-event" aria-label={eventLabel(event)} title={eventLabel(event)} style={{ top: startMinute * HOUR_H / 60, height: Math.max(44, (endMinute - startMinute) * HOUR_H / 60), background: colorBg(event.color), borderLeftColor: event.color || 'var(--primary)' }} onClick={(click) => { click.stopPropagation(); onEventClick(event); }}>
          <span className="time-event-title">{fmtTime(event.start)} · {event.title}</span><span className="calendar-event-meta">{eventMeta(event)}</span>
        </button>;
      })}</div>)}</div>
    </div></div>
  </div></div>;
}

function AgendaView({ events, onEventClick }: { events: CalendarEventDto[]; onEventClick: (event: CalendarEventDto) => void }) {
  const sorted = [...events].sort((a, b) => eventStart(a).getTime() - eventStart(b).getTime() || a.title.localeCompare(b.title));
  return <div className="card calendar-agenda">{sorted.map((event) => <button key={eventKey(event)} className="calendar-agenda-item" aria-label={eventLabel(event)} onClick={() => onEventClick(event)}>
    <span className="event-dot" style={{ background: event.color || 'var(--primary)' }} /><span className="grow"><strong>{event.title}</strong><span className="calendar-event-meta">{eventMeta(event)}{event.projectCode ? ' · ' + event.projectCode : ''}</span>{event.description && <span className="muted small calendar-agenda-description">{event.description}</span>}</span>
    {event.priority != null && <span className="badge">{PRIORITY_LABELS[event.priority]}</span>}{event.assigneeName && <span className="avatar" title={event.assigneeName}>{initials(event.assigneeName)}</span>}<span className="calendar-agenda-date muted small">{eventDates(event)}</span>
  </button>)}{sorted.length === 0 && <div className="empty">Sin fechas en este periodo.</div>}</div>;
}

function EventChip({ event, onClick }: { event: CalendarEventDto; onClick: () => void }) {
  return <button className="cal-chip" aria-label={eventLabel(event)} title={eventLabel(event)} style={{ borderLeftColor: event.color || 'var(--primary)', background: colorBg(event.color) }} onClick={(click) => { click.stopPropagation(); onClick(); }}>
    <span className="calendar-chip-title">{!event.isAllDay && fmtTime(event.start) + ' '}{event.title}</span><span className="calendar-event-meta">{eventMeta(event)}</span>
  </button>;
}

function toLocalInput(date: Date) { return `${dateKey(date)}T${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`; }
function inclusiveEnd(start: Date, end: Date) { return end > start && end.getHours() === 0 && end.getMinutes() === 0 ? addDays(startOfDay(end), -1) : startOfDay(end); }

function EventModal({ initial, projects, timeZoneId, onClose, onSaved, onDeleted }: {
  initial: EditorState; projects: ProjectDto[]; timeZoneId: string; onClose: () => void; onSaved: (event: CalendarEventDto) => void; onDeleted: () => void;
}) {
  const editing = !!initial.event;
  const canEdit = !editing || initial.event?.canEdit !== false;
  const canDelete = editing && initial.event?.canDelete !== false;
  const [title, setTitle] = useState(initial.event?.title || '');
  const [description, setDescription] = useState(initial.event?.description || '');
  const [type, setType] = useState(initial.event?.type ?? 0);
  const [priority, setPriority] = useState(initial.event?.priority ?? 1);
  const [color, setColor] = useState(initial.event?.color || TYPE_DEFAULT_COLOR[initial.event?.type ?? 0]);
  const [projectId, setProjectId] = useState(initial.event?.projectId || '');
  const [assigneeId, setAssigneeId] = useState(initial.event?.assigneeId || '');
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [allDay, setAllDay] = useState(initial.event?.isAllDay ?? false);
  const [start, setStart] = useState(initial.event?.isAllDay ? dateKey(initial.start) : toLocalInput(initial.start));
  const [end, setEnd] = useState(initial.event?.isAllDay ? dateKey(addDays(initial.end, -1)) : toLocalInput(initial.end));
  const [error, setError] = useState('');
  const [memberError, setMemberError] = useState('');
  const [memberRetry, setMemberRetry] = useState(0);
  const [busy, setBusy] = useState(false);
  const saving = useRef(false);
  const dialog = useRef<HTMLDivElement>(null);
  const closeButton = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    let current = true; setMembers([]); setMemberError('');
    if (projectId) void api.get<MemberDto[]>(`/api/projects/${projectId}/members`).then((result) => { if (current) setMembers(result.filter((member) => member.isActive)); }).catch((failure) => { if (current) setMemberError('No se pudieron cargar los responsables: ' + (failure as Error).message); });
    return () => { current = false; };
  }, [projectId, memberRetry]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    if (canEdit) dialog.current?.querySelector<HTMLInputElement>('#calendar-event-title')?.focus(); else closeButton.current?.focus();
    return () => { if (previous?.isConnected) previous.focus(); };
  }, [canEdit]);
  const toggleAllDay = (checked: boolean) => {
    if (checked) {
      const first = new Date(start), last = new Date(end);
      setStart(start.slice(0, 10)); setEnd(dateKey(inclusiveEnd(first, last)));
    } else { setStart(start.slice(0, 10) + 'T09:00'); setEnd(end.slice(0, 10) + 'T10:00'); }
    setAllDay(checked);
  };
  const submit = async (event: FormEvent) => {
    event.preventDefault(); if (saving.current || !canEdit) return;
    if (!title.trim() || !start || !end) { setError('Completa el título y las fechas del evento.'); return; }
    const first = allDay ? parseCalendarDate(start) : new Date(start);
    const last = allDay ? addDays(parseCalendarDate(end), 1) : new Date(end);
    if (!Number.isFinite(first.getTime()) || !Number.isFinite(last.getTime()) || last <= first) { setError('La fecha final debe ser posterior al inicio; un evento de todo el día puede comenzar y terminar el mismo día.'); return; }
    saving.current = true; setBusy(true); setError('');
    const payload = { title: title.trim(), description, type, priority, color, projectId: projectId || null, assigneeId: assigneeId || null, isAllDay: allDay,
      start: allDay ? dateKey(first) + 'T00:00:00' : first.toISOString(), end: allDay ? dateKey(last) + 'T00:00:00' : last.toISOString(), timeZoneId,
      recurrence: initial.event?.recurrence ?? null, linkedWorkItemId: initial.event?.linkedWorkItemId ?? null,
      linkedMilestoneId: initial.event?.linkedMilestoneId ?? null, linkedReleaseId: initial.event?.linkedReleaseId ?? null };
    try { onSaved(editing ? await api.put<CalendarEventDto>(`/api/calendar/${initial.event!.id}`, payload) : await api.post<CalendarEventDto>('/api/calendar', payload)); }
    catch (failure) { setError((failure as Error).message); }
    finally { saving.current = false; setBusy(false); }
  };
  const remove = async () => {
    if (!canDelete || saving.current || !window.confirm('¿Eliminar este evento?')) return;
    saving.current = true; setBusy(true); setError('');
    try { await api.del(`/api/calendar/${initial.event!.id}`); onDeleted(); }
    catch (failure) { setError('No se pudo eliminar el evento: ' + (failure as Error).message); }
    finally { saving.current = false; setBusy(false); }
  };
  return <div className="modal-backdrop" onClick={() => { if (!busy) onClose(); }}><div className="modal card calendar-event-modal" role="dialog" aria-modal="true" aria-labelledby="calendar-editor-title" ref={dialog} onClick={(event) => event.stopPropagation()} onKeyDown={(event) => {
    if (event.key === 'Escape' && !busy) onClose();
    if (event.key !== 'Tab') return;
    const controls = Array.from(dialog.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), textarea:not(:disabled), select:not(:disabled)') || []).filter((control) => !control.closest('fieldset:disabled'));
    const first = controls[0], last = controls[controls.length - 1];
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
  }}>
    <div className="row"><h2 id="calendar-editor-title">{editing ? canEdit ? 'Editar evento' : 'Detalle del evento' : 'Nuevo evento'}</h2><span className="grow" /><button ref={closeButton} type="button" className="btn sm" disabled={busy} aria-label="Cerrar evento" onClick={onClose}>✕</button></div>
    {error && <div className="alert error" role="alert">{error}</div>}
    {memberError && <div className="alert warn" role="alert">{memberError} <button className="btn sm" onClick={() => setMemberRetry((value) => value + 1)}>Reintentar responsables</button></div>}
    <form onSubmit={submit}><fieldset className="calendar-event-fields" disabled={busy || !canEdit}>
      <div className="field"><label htmlFor="calendar-event-title">Título</label><input id="calendar-event-title" className="input" value={title} onChange={(event) => setTitle(event.target.value)} maxLength={250} required /></div>
      <div className="field"><label htmlFor="calendar-event-description">Nota / descripción</label><textarea id="calendar-event-description" className="input" value={description} onChange={(event) => setDescription(event.target.value)} /></div>
      <div className="calendar-editor-grid"><div className="field"><label htmlFor="calendar-event-type">Tipo</label><select id="calendar-event-type" className="input" value={type} onChange={(event) => { const next = Number(event.target.value); setType(next); if (!initial.event?.color) setColor(TYPE_DEFAULT_COLOR[next]); }}>{Object.entries(TYPE_LABELS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></div>
        <div className="field"><label htmlFor="calendar-event-priority">Prioridad</label><select id="calendar-event-priority" className="input" value={priority} onChange={(event) => setPriority(Number(event.target.value))}>{PRIORITY_LABELS.map((label, index) => <option key={label} value={index}>{label}</option>)}</select></div></div>
      <div className="field"><span>Color</span><div className="row">{EVENT_COLORS.map((candidate) => <button key={candidate} type="button" aria-label={'Elegir color ' + candidate} aria-pressed={color === candidate} className={`color-swatch ${color === candidate ? 'active' : ''}`} style={{ background: candidate }} onClick={() => setColor(candidate)} />)}</div></div>
      <div className="calendar-editor-grid"><div className="field"><label htmlFor="calendar-event-project">Proyecto</label><select id="calendar-event-project" className="input" value={projectId} onChange={(event) => { setProjectId(event.target.value); setAssigneeId(''); }}><option value="">Sin proyecto</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}</select></div>
        <div className="field"><label htmlFor="calendar-event-assignee">Responsable</label><select id="calendar-event-assignee" className="input" value={assigneeId} onChange={(event) => setAssigneeId(event.target.value)} disabled={!projectId || !!memberError}><option value="">Sin responsable</option>{assigneeId && !members.some((member) => member.userId === assigneeId) && <option value={assigneeId}>{initial.event?.assigneeName || 'Responsable actual'}</option>}{members.map((member) => <option key={member.userId} value={member.userId}>{member.name}</option>)}</select></div></div>
      <label className="row small calendar-allday-toggle"><input type="checkbox" checked={allDay} onChange={(event) => toggleAllDay(event.target.checked)} />Todo el día</label>
      <div className="calendar-editor-grid"><div className="field"><label htmlFor="calendar-event-start">Inicio</label><input id="calendar-event-start" className="input" type={allDay ? 'date' : 'datetime-local'} value={start} onChange={(event) => setStart(event.target.value)} required /></div>
        <div className="field"><label htmlFor="calendar-event-end">{allDay ? 'Último día (incluido)' : 'Fin'}</label><input id="calendar-event-end" className="input" type={allDay ? 'date' : 'datetime-local'} value={end} onChange={(event) => setEnd(event.target.value)} required /></div></div>
    </fieldset><div className="row calendar-editor-actions">{canDelete && <button type="button" className="btn danger" disabled={busy} onClick={() => void remove()}>Eliminar</button>}<span className="grow" /><button type="button" className="btn" disabled={busy} onClick={onClose}>{canEdit ? 'Cancelar' : 'Cerrar'}</button>{canEdit && <button className="btn primary" disabled={busy}>{busy ? 'Guardando…' : editing ? 'Guardar' : 'Crear'}</button>}</div></form>
  </div></div>;
}
