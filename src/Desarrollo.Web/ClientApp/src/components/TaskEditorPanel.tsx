import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import type { DiagramTaskDto, MemberDto, TaskDiagramDto, WorkItemDetailDto, WorkflowStateDto } from '../types';
import { PRIORITY_LABELS, TYPE_LABELS } from '../constants';
import './task-editor.css';

interface Props {
  projectId: string;
  task?: WorkItemDetailDto;
  parent?: DiagramTaskDto;
  states: WorkflowStateDto[];
  onClose: () => void;
  onSaved: (task: WorkItemDetailDto) => void;
}

const accents = ['#A76BF3', '#4B9EFF', '#3AC5CA'];
const accentNames = ['Violeta', 'Azul', 'Turquesa'];

/** Shared by the diagram and the full task detail. Failed writes never discard the draft. */
export default function TaskEditorPanel({ projectId, task, parent, states, onClose, onSaved }: Props) {
  const [baseTask, setBaseTask] = useState(task);
  const [title, setTitle] = useState(task?.title ?? '');
  const [type, setType] = useState(task?.type ?? 0);
  const [description, setDescription] = useState(task?.description ?? '');
  const [notes, setNotes] = useState(task?.notes ?? '');
  const [priority, setPriority] = useState<number>(task?.priority ?? 1);
  const [stateId, setStateId] = useState(task?.stateId ?? states.find(s => s.isDefault)?.id ?? states[0]?.id ?? '');
  const [accent, setAccent] = useState(task?.accentColor ?? accents[parent ? Math.min(parent.depth + 1, 2) : 0]);
  const [progress, setProgress] = useState(task?.progressPercent == null ? '' : String(task.progressPercent));
  const [assigneeId, setAssigneeId] = useState(task?.assigneeId ?? '');
  const [dueDate, setDueDate] = useState(task?.dueDate?.slice(0, 10) ?? '');
  const [acceptance, setAcceptance] = useState(task?.acceptanceCriteria ?? '');
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [candidates, setCandidates] = useState<DiagramTaskDto[]>([]);
  const [nextParent, setNextParent] = useState(task?.parentTaskId ?? '');
  const [permissions, setPermissions] = useState<number[]>([]);
  const [projectName, setProjectName] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [contextError, setContextError] = useState('');
  const [remote, setRemote] = useState<WorkItemDetailDto | null>(null);
  const [parentNotice, setParentNotice] = useState('');
  const requestId = useRef(crypto.randomUUID());
  const dialogRef = useRef<HTMLElement>(null);
  const savingRef = useRef(false);
  const hierarchySavedRef = useRef(false);
  const closeRef = useRef(onClose);
  const finishClose = () => {
    if (hierarchySavedRef.current && baseTask) onSaved(baseTask);
    else onClose();
  };
  closeRef.current = finishClose;

  useEffect(() => {
    let active = true;
    const load = async () => {
      try {
        const [people, diagram] = await Promise.all([
          api.get<MemberDto[]>(`/api/projects/${projectId}/members`),
          api.get<TaskDiagramDto>(`/api/projects/${projectId}/task-diagram`),
        ]);
        if (!active) return;
        setMembers(people.filter(m => m.isActive));
        setCandidates(diagram.tasks);
        setPermissions(diagram.permissions);
        setProjectName(diagram.projectName);
      } catch (e) { if (active) setContextError((e as Error).message); }
    };
    void load();
    return () => { active = false; };
  }, [projectId]);

  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    dialogRef.current?.querySelector<HTMLInputElement>('input')?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') { e.preventDefault(); if (!savingRef.current) closeRef.current(); }
      if (e.key !== 'Tab') return;
      const elements = Array.from(dialogRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), a[href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]') ?? [])
        .filter(el => el.offsetParent !== null);
      const first = elements[0], last = elements[elements.length - 1];
      if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last?.focus(); }
      else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first?.focus(); }
    };
    document.addEventListener('keydown', onKey);
    return () => { document.removeEventListener('keydown', onKey); document.body.style.overflow = overflow; previous?.focus(); };
  }, []);

  const handleError = async (e: unknown) => {
    const failure = e as Error & { status?: number };
    setError(failure.message);
    if (failure.status === 409 && baseTask) {
      try { setRemote(await api.get<WorkItemDetailDto>(`/api/workitems/${baseTask.id}`)); }
      catch { setError(`${failure.message} No se pudo cargar la versión actual. Tu borrador se conserva.`); }
    }
  };

  const save = async (e: FormEvent) => {
    e.preventDefault();
    if (savingRef.current || remote || !title.trim()) return;
    savingRef.current = true; setBusy(true); setError('');
    try {
      const fields = { title: title.trim(), type, priority, description, notes, stateId,
        accentColor: accent, progressPercent: progress === '' ? null : Number(progress),
        assigneeId: assigneeId || null, dueDate: dueDate || null };
      let saved: WorkItemDetailDto;
      if (baseTask) {
        saved = await api.put<WorkItemDetailDto>(`/api/workitems/${baseTask.id}`, {
          ...baseTask, ...fields, acceptanceCriteria: acceptance, rowVersion: baseTask.rowVersion,
          clearProgressPercent: progress === '',
        });
      } else {
        saved = await api.post<WorkItemDetailDto>(parent ? `/api/workitems/${parent.id}/children` : '/api/workitems', {
          ...fields, projectId, clientRequestId: requestId.current,
        });
      }
      onSaved(saved);
    } catch (e) { await handleError(e); }
    finally { savingRef.current = false; setBusy(false); }
  };

  const changeParent = async () => {
    if (!baseTask || savingRef.current || remote) return;
    savingRef.current = true; setBusy(true); setError(''); setParentNotice('');
    try {
      const updated = await api.patch<WorkItemDetailDto>(`/api/workitems/${baseTask.id}/parent`, {
        parentTaskId: nextParent || null, rowVersion: baseTask.rowVersion,
      });
      setBaseTask(updated);
      hierarchySavedRef.current = true;
      setParentNotice('Madre actualizada. Los campos que estás editando siguen en tu borrador.');
    } catch (e) { await handleError(e); }
    finally { savingRef.current = false; setBusy(false); }
  };

  const descendants = new Set<string>(baseTask ? [baseTask.id] : []);
  let changed = true;
  while (changed) {
    changed = false;
    for (const candidate of candidates) if (candidate.parentTaskId && descendants.has(candidate.parentTaskId) && !descendants.has(candidate.id)) {
      descendants.add(candidate.id); changed = true;
    }
  }
  const allowedParents = candidates.filter(c => !c.isArchived && !descendants.has(c.id));
  const editing = !!baseTask;
  const canSave = !baseTask?.isArchived && permissions.includes(editing ? 3 : 2);

  return (
    <div className="task-editor-backdrop">
      <aside className="task-editor-panel" role="dialog" aria-modal="true" aria-labelledby="task-editor-title" ref={dialogRef}>
        <header className="task-editor-header">
          <div><div className="muted small">{projectName || 'Proyecto actual'}{baseTask ? ` · ${baseTask.code}` : ''}</div>
            <h2 id="task-editor-title">{editing ? 'Editar tarea' : parent ? 'Crear hija' : 'Nueva tarjeta'}</h2></div>
          <button type="button" className="btn" aria-label="Cerrar editor" disabled={busy} onClick={finishClose}>✕</button>
        </header>
        <form onSubmit={save} className="task-editor-form">
          <div className="task-editor-body">
            {(parent || baseTask?.fullPath) && <p className="task-editor-path">{parent ? `Madre: ${parent.fullPath || parent.displayTitle}` : baseTask?.fullPath}</p>}
            {baseTask?.isArchived && <div className="alert warn">Esta tarea está archivada. Restáurala desde el mapa para modificarla.</div>}
            {editing && !permissions.includes(3) && <p className="muted small">Consulta de tarea · solo lectura.</p>}
            {contextError && <div className="alert warn" role="status">No se pudo cargar el contexto: {contextError}</div>}
            {error && <div className="alert error" role="alert">{error}</div>}
            {remote && <section className="task-editor-conflict" aria-label="Conflicto de edición">
              <h3>Otra persona actualizó esta tarea</h3>
              <p>Tu borrador sigue aquí. Revisa los datos guardados antes de reintentar.</p>
              <dl><dt>Título actual</dt><dd>{remote.title}</dd><dt>Descripción actual</dt><dd>{remote.description || 'Sin descripción'}</dd>
                <dt>Nota actual</dt><dd>{remote.notes || 'Sin nota'}</dd><dt>Prioridad y estado</dt><dd>{PRIORITY_LABELS[remote.priority]} · {remote.stateName}</dd>
                <dt>Progreso manual</dt><dd>{remote.progressPercent == null ? 'Sin valor' : `${remote.progressPercent}%`}</dd></dl>
              <button type="button" className="btn" onClick={() => { setBaseTask(remote); setRemote(null); setError(''); }}>
                Conservar mi borrador y preparar reintento
              </button>
            </section>}
            <fieldset disabled={busy || !canSave} className="task-editor-fields">
              <div className="field"><label htmlFor="task-name">Nombre propio</label><input id="task-name" className="input" value={title} onChange={e => setTitle(e.target.value)} maxLength={250} required /></div>
              <div className="task-editor-grid">
                <div className="field"><label htmlFor="task-type">Tipo de trabajo</label><select id="task-type" className="input" value={type} onChange={e => setType(Number(e.target.value) as typeof type)}>{TYPE_LABELS.map((label, i) => <option key={label} value={i}>{label}</option>)}</select></div>
                <div className="field"><label htmlFor="task-priority">Prioridad</label><select id="task-priority" className="input" value={priority} onChange={e => setPriority(Number(e.target.value))}>{PRIORITY_LABELS.map((label, i) => <option key={label} value={i}>{label}</option>)}</select></div>
              </div>
              <div className="field"><label htmlFor="task-state">Estado</label><select id="task-state" className="input" value={stateId} onChange={e => setStateId(e.target.value)} disabled={editing && !permissions.includes(5)} required>{states.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></div>
              <div className="field"><label htmlFor="task-description">Descripción del trabajo</label><textarea id="task-description" className="input" rows={4} value={description} onChange={e => setDescription(e.target.value)} maxLength={20000} /></div>
              <div className="field"><label htmlFor="task-notes">Nota</label><textarea id="task-notes" className="input" rows={3} value={notes} onChange={e => setNotes(e.target.value)} maxLength={4000} /></div>
              <div className="task-editor-grid">
                <div className="field"><label htmlFor="task-assignee">Responsable</label><select id="task-assignee" className="input" value={assigneeId} onChange={e => setAssigneeId(e.target.value)} disabled={!permissions.includes(4)}><option value="">Sin asignar</option>{members.map(m => <option key={m.userId} value={m.userId}>{m.name}</option>)}</select></div>
                <div className="field"><label htmlFor="task-due">Vencimiento</label><input id="task-due" type="date" className="input" value={dueDate} onChange={e => setDueDate(e.target.value)} /></div>
              </div>
              <div className="field"><label htmlFor="task-progress">Progreso manual (opcional)</label><input id="task-progress" className="input" type="number" min={0} max={100} step={1} value={progress} onChange={e => setProgress(e.target.value)} aria-describedby="task-progress-help" /><small id="task-progress-help" className="muted">Si la tarea tiene elementos de checklist, el mapa utiliza su avance calculado. Este valor no cambia prioridad ni estado.</small></div>
              <div className="field"><span id="task-accent-label">Acento visual</span><div className="task-editor-accents" role="group" aria-labelledby="task-accent-label">{accents.map((color, i) => <button key={color} type="button" aria-pressed={accent === color} className={`task-editor-accent ${accent === color ? 'active' : ''}`} onClick={() => setAccent(color)}><i style={{ background: color }} />{accentNames[i]}</button>)}</div></div>
              {editing && <div className="field"><label htmlFor="task-acceptance">Criterios de aceptación</label><textarea id="task-acceptance" className="input" rows={3} value={acceptance} onChange={e => setAcceptance(e.target.value)} /></div>}
            </fieldset>
            {baseTask && !baseTask.isArchived && permissions.includes(3) && <section className="task-editor-hierarchy">
              <h3>Cambiar madre</h3><p className="muted small">Esta acción guarda únicamente la jerarquía. Mover una tarjeta en el lienzo conserva su madre.</p>
              <label htmlFor="task-parent">Madre inmediata</label>
              <select id="task-parent" className="input" value={nextParent} onChange={e => setNextParent(e.target.value)} disabled={busy || !!remote}><option value="">Ninguna · convertir en raíz</option>{allowedParents.map(c => <option key={c.id} value={c.id}>{c.code} · {c.displayTitle}</option>)}</select>
              <button type="button" className="btn mt-8" disabled={busy || !!remote || nextParent === (baseTask.parentTaskId ?? '')} onClick={() => void changeParent()}>Aplicar cambio de madre</button>
              {parentNotice && <p role="status" className="small">{parentNotice}</p>}
            </section>}
          </div>
          <footer className="task-editor-footer">
            {baseTask && <Link to={`/tasks/${baseTask.id}`} className="small" onClick={finishClose}>Abrir ficha completa</Link>}
            <span className="grow" /><button type="button" className="btn" disabled={busy} onClick={finishClose}>Cancelar</button>
            {canSave && <button className="btn primary" disabled={busy || !!remote}>{busy ? 'Guardando…' : 'Guardar'}</button>}
          </footer>
        </form>
      </aside>
    </div>
  );
}
