import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import type { WorkItemDetailDto, ChecklistDto, CommentDto, WorkflowStateDto, TaskDiagramDto } from '../types';
import { PRIORITY_LABELS, TYPE_LABELS, SEVERITY_LABELS, fmtDateTime, initials } from '../constants';
import TaskEditorPanel from '../components/TaskEditorPanel';

export default function TaskDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [task, setTask] = useState<WorkItemDetailDto | null>(null);
  const [checklists, setChecklists] = useState<ChecklistDto[]>([]);
  const [comments, setComments] = useState<CommentDto[]>([]);
  const [transitions, setTransitions] = useState<WorkflowStateDto[]>([]);
  const [states, setStates] = useState<WorkflowStateDto[]>([]);
  const [permissions, setPermissions] = useState<number[]>([]);
  const [error, setError] = useState('');
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const requestSequence = useRef(0);
  const activeTaskId = useRef(id);
  activeTaskId.current = id;

  const load = async () => {
    if (!id) return;
    const sequence = ++requestSequence.current;
    const current = () => sequence === requestSequence.current && activeTaskId.current === id;
    try {
      const [t, c, cm, tr] = await Promise.all([
        api.get<WorkItemDetailDto>('/api/workitems/' + id),
        api.get<ChecklistDto[]>('/api/workitems/' + id + '/checklists'),
        api.get<CommentDto[]>('/api/workitems/' + id + '/comments'),
        api.get<WorkflowStateDto[]>('/api/workitems/' + id + '/transitions'),
      ]);
      if (!current()) return;
      if (t.projectId) {
        const diagram = await api.get<TaskDiagramDto>('/api/projects/' + t.projectId + '/task-diagram');
        if (!current()) return;
        setPermissions(diagram.permissions); setStates(diagram.states);
      }
      setTask(t); setChecklists(c); setComments(cm); setTransitions(tr); setError('');
    } catch (e) { if (current()) setError((e as Error).message); }
  };
  useEffect(() => { setTask(null); setError(''); setEditing(false); void load(); return () => { requestSequence.current++; }; }, [id]);

  const changeState = async (stateId: string) => {
    if (!task || busy) return;
    setBusy(true); setError('');
    try { await api.post('/api/workitems/' + id + '/transition', { toStateId: stateId, rowVersion: task.rowVersion }); await load(); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  };

  if (!task) return error ? <div className="alert error" role="alert">{error}<button className="btn" onClick={() => void load()}>Reintentar</button></div> : <div className="empty">Cargando…</div>;
  const editable = permissions.includes(3) && !task.isArchived;
  const checklistItems = checklists.flatMap(c => c.items);
  const checklistProgress = checklistItems.length ? Math.round(checklistItems.filter(i => i.isCompleted).length / checklistItems.length * 100) : null;

  return (
    <div>
      <div className="row" style={{ marginBottom: 8, flexWrap: 'wrap' }}>
        <Link className="muted small" to={task.projectId ? '/projects/' + task.projectId + '/board' : '/projects'}>← Tablero</Link>
        {task.projectId && <><Link className="muted small" to={'/projects/' + task.projectId + '/table'}>Lista</Link><Link className="muted small" to={'/projects/' + task.projectId + '/diagram'}>Mapa de tareas</Link></>}
      </div>
      <div className="row" style={{ marginBottom: 16 }}>
        <div><div className="muted small">{task.code} · {TYPE_LABELS[task.type]}</div><h1 className="page-title" style={{ margin: 0 }}>{task.title}</h1>
          <div className="muted small">{task.fullPath}</div></div>
        <div className="grow" />
        {editable && <button className="btn" onClick={() => setEditing(true)}>Editar</button>}
      </div>
      {error && <div className="alert error" role="alert">{error} <button className="btn sm" onClick={() => void load()}>Actualizar datos</button></div>}
      {task.isArchived && <div className="alert warn">Tarea archivada. Puedes restaurarla desde «Mostrar archivadas» en el mapa.</div>}
      <div className="two-col">
        <div className="detail-main">
          <section className="card" style={{ marginBottom: 16 }}>
            <div className="row"><span className="badge">{PRIORITY_LABELS[task.priority]}</span><span className="badge primary">{task.stateName}</span>
              {task.isBlocked && <span className="badge error">Bloqueada</span>}{task.assigneeName && <span className="badge">👤 {task.assigneeName}</span>}
              <span className="grow" /><span className="muted small">Actualizado {fmtDateTime(task.updatedAt)}</span></div>
            {task.isBlocked && task.blockReason && <div className="alert warn mt-16">{task.blockReason}</div>}
            {task.description && <p className="mt-16" style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{task.description}</p>}
            {task.notes && <div className="card mt-16" style={{ background: 'var(--panel-2)' }}><h3>Nota</h3><p style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{task.notes}</p></div>}
            {checklistProgress !== null ? <p className="small mt-16">Progreso de checklist: {checklistProgress}%</p> : task.progressPercent != null && <p className="small mt-16">Progreso manual: {task.progressPercent}%</p>}
            {task.type === 3 && <div className="mt-16"><h4>Reproducción / análisis</h4>
              {task.reproSteps && <p className="small"><strong>Pasos:</strong> {task.reproSteps}</p>}{task.expectedResult && <p className="small"><strong>Esperado:</strong> {task.expectedResult}</p>}
              {task.actualResult && <p className="small"><strong>Observado:</strong> {task.actualResult}</p>}{task.environment && <p className="small"><strong>Ambiente:</strong> {task.environment}</p>}
              {task.severity != null && <p className="small"><strong>Severidad:</strong> {SEVERITY_LABELS[task.severity]}</p>}</div>}
            {task.type === 4 && <div className="mt-16">{task.researchQuestion && <p className="small"><strong>Pregunta:</strong> {task.researchQuestion}</p>}{task.researchConclusion && <p className="small"><strong>Conclusión:</strong> {task.researchConclusion}</p>}</div>}
            {task.acceptanceCriteria && <div className="mt-16"><h4>Criterios de aceptación</h4><p className="small muted" style={{ whiteSpace: 'pre-wrap' }}>{task.acceptanceCriteria}</p></div>}
          </section>
          <section className="card" style={{ marginBottom: 16 }}><h3>Checklists</h3>
            {checklists.map(c => <ChecklistBlock key={c.id} c={c} editable={editable} onChanged={load} />)}
            {checklists.length === 0 && <div className="muted small">Sin checklists.</div>}
          </section>
          <section className="card"><h3>Comentarios ({comments.length})</h3>
            {comments.map(c => <div key={c.id} style={{ padding: '8px 0', borderBottom: '1px solid var(--border)' }}><div className="row"><span className="avatar">{initials(c.authorName)}</span><span className="small" style={{ fontWeight: 600 }}>{c.authorName || 'Usuario'}</span><span className="muted small">{fmtDateTime(c.createdAt)}</span></div><p style={{ margin: '6px 0 0', whiteSpace: 'pre-wrap' }}>{c.body}</p></div>)}
            {permissions.includes(1) && !task.isArchived && <CommentForm taskId={id!} onAdded={load} />}
          </section>
        </div>
        <div className="detail-side">
          {permissions.includes(5) && !task.isArchived && <section className="card" style={{ marginBottom: 16 }}><h3>Cambiar estado</h3><div className="stack">
            {transitions.filter(s => s.id !== task.stateId).map(s => <button key={s.id} className="btn" disabled={busy} style={{ justifyContent: 'flex-start' }} onClick={() => void changeState(s.id)}><span className="state-dot" style={{ background: s.color || 'var(--text-2)' }} />{s.name}</button>)}
          </div></section>}
          {editable && <section className="card"><h3>Acciones</h3><div className="stack"><button className="btn" disabled={busy} onClick={async () => {
            setBusy(true); try { await api.post('/api/workitems/' + id + '/block', { blocked: !task.isBlocked, reason: task.isBlocked ? null : prompt('Motivo del bloqueo') || undefined }); await load(); }
            catch (e) { setError((e as Error).message); } finally { setBusy(false); }
          }}>{task.isBlocked ? 'Desbloquear' : 'Bloquear'}</button></div></section>}
        </div>
      </div>
      {editing && task.projectId && <TaskEditorPanel task={task} states={states} projectId={task.projectId} onClose={() => setEditing(false)} onSaved={() => { setEditing(false); void load(); }} />}
    </div>
  );
}

function ChecklistBlock({ c, editable, onChanged }: { c: ChecklistDto; editable: boolean; onChanged: () => Promise<void> }) {
  const [text, setText] = useState(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const add = async (e: FormEvent) => {
    e.preventDefault(); if (!text.trim() || busy) return;
    setBusy(true); setError('');
    try { await api.post('/api/workitems/checklist-items', { checklistId: c.id, text }); setText(''); await onChanged(); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return <div style={{ marginBottom: 12 }}><div className="row"><strong>{c.title}</strong><span className="muted small">{c.items.filter(i => i.isCompleted).length}/{c.items.length}</span></div>
    {error && <div className="alert error" role="alert">{error}</div>}
    {c.items.map(i => <label key={i.id} className="row" style={{ padding: '3px 0' }}><input type="checkbox" checked={i.isCompleted} disabled={!editable || busy} onChange={async () => {
      setBusy(true); setError(''); try { await api.post('/api/workitems/checklist-items/' + i.id + '/toggle', {}); await onChanged(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
    }} /><span style={{ textDecoration: i.isCompleted ? 'line-through' : 'none' }}>{i.text}</span></label>)}
    {editable && <form onSubmit={add} className="row mt-8"><input className="input grow" aria-label="Nuevo elemento de checklist" placeholder="Añadir elemento…" value={text} onChange={e => setText(e.target.value)} disabled={busy} /><button className="btn sm" disabled={busy} aria-label="Añadir elemento">+</button></form>}
  </div>;
}

function CommentForm({ taskId, onAdded }: { taskId: string; onAdded: () => Promise<void> }) {
  const [body, setBody] = useState(''); const [error, setError] = useState(''); const [busy, setBusy] = useState(false);
  const submit = async (e: FormEvent) => {
    e.preventDefault(); if (!body.trim() || busy) return; setBusy(true); setError('');
    try { await api.post('/api/workitems/' + taskId + '/comments', { body }); setBody(''); await onAdded(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  };
  return <form onSubmit={submit} className="mt-16">{error && <div className="alert error" role="alert">{error}</div>}<textarea className="input" style={{ width: '100%' }} aria-label="Comentario" placeholder="Escribe un comentario…" value={body} onChange={e => setBody(e.target.value)} disabled={busy} /><button className="btn primary sm mt-8" disabled={busy}>{busy ? 'Guardando…' : 'Comentar'}</button></form>;
}
