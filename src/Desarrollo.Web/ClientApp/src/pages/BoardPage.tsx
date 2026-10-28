import { useEffect, useRef, useState, DragEvent } from 'react';
import { useNavigate, useParams, Link } from 'react-router-dom';
import { api, qs } from '../api';
import type { Paged, WorkItemCardDto, WorkflowStateDto, TaskDiagramDto } from '../types';
import TaskEditorPanel from '../components/TaskEditorPanel';
import { PRIORITY_LABELS, PRIORITY_CLASS, fmtDate, isOverdue } from '../constants';

export default function BoardPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [states, setStates] = useState<WorkflowStateDto[]>([]);
  const [items, setItems] = useState<WorkItemCardDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);
  const [dragId, setDragId] = useState<string | null>(null);
  const [error, setError] = useState('');
  const [permissions, setPermissions] = useState<number[]>([]);
  const requestSequence = useRef(0);
  const activeProjectId = useRef(id);
  const changingState = useRef(false);
  activeProjectId.current = id;

  const load = async () => {
    if (!id) return;
    const sequence = ++requestSequence.current;
    const current = () => sequence === requestSequence.current && activeProjectId.current === id;
    setLoading(true);
    try {
    const [s, w, diagram] = await Promise.all([
      api.get<WorkflowStateDto[]>(`/api/projects/${id}/states`),
      api.get<Paged<WorkItemCardDto>>(`/api/workitems${qs({ projectId: id, pageSize: 500 })}`),
      api.get<TaskDiagramDto>(`/api/projects/${id}/task-diagram`),
    ]);
    if (!current()) return;
    setStates(s);
    setItems(w.items);
    setPermissions(diagram.permissions);
    setError('');
    } catch (e) { if (current()) setError((e as Error).message); } finally { if (current()) setLoading(false); }
  };

  useEffect(() => { setItems([]); setPermissions([]); void load(); return () => { requestSequence.current++; }; }, [id]);

  const onDrop = async (e: DragEvent, stateId: string) => {
    e.preventDefault();
    if (changingState.current || !permissions.includes(5)) return;
    const workId = e.dataTransfer.getData('text/plain') || dragId;
    if (!workId) return;
    changingState.current = true;
    setDragId(null);
    try {
      const item = items.find(t => t.id === workId);
      if (!item) return;
      await api.post(`/api/workitems/${workId}/transition`, { toStateId: stateId, rowVersion: item.rowVersion });
      await load();
    } catch (err) {
      setError((err as Error).message);
    } finally { changingState.current = false; }
  };

  return (
    <div className="board-wrap">
      <div className="row" style={{ marginBottom: 14 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Tablero</h1>
        <div className="grow" />
        <Link className="btn" to={`/projects/${id}`}>Resumen</Link>
        <Link className="btn" to={`/projects/${id}/table`}>Lista</Link>
        <Link className="btn" to={`/projects/${id}/diagram`}>Diagrama</Link>
        {permissions.includes(2) && <button className="btn primary" onClick={() => setShowCreate(true)}>+ Nueva tarea</button>}
      </div>
      {error && <div className="alert error" role="alert">{error} <button className="btn sm" onClick={() => void load()}>Actualizar datos</button></div>}

      {loading ? <div className="empty">Cargando…</div> : (
        <div className="board">
          {states.map((s) => {
            const colItems = items.filter((i) => i.stateId === s.id);
            return (
              <div key={s.id} className="board-col"
                onDragOver={(e) => e.preventDefault()}
                onDrop={(e) => onDrop(e, s.id)}>
                <div className="board-col-head">
                  <span className="state-dot" style={{ background: s.color || 'var(--text-2)' }} />
                  <span className="grow">{s.name}</span>
                  <span className="muted small">{colItems.length}</span>
                </div>
                <div className="board-col-body">
                  {colItems.map((t) => (
                    <div key={t.id} className="task-card" draggable={permissions.includes(5)} role="button" tabIndex={0}
                      onKeyDown={(e) => { if (e.key === 'Enter') navigate(`/tasks/${t.id}`); }}
                      onDragStart={(e) => { e.dataTransfer.setData('text/plain', t.id); setDragId(t.id); }}
                      onClick={() => navigate(`/tasks/${t.id}`)}>
                      <div className="row">
                        <span className="muted small">{t.code}</span>
                        <span className="grow" />
                        <span className={`badge ${PRIORITY_CLASS[t.priority]}`}>{PRIORITY_LABELS[t.priority]}</span>
                      </div>
                      <div className="task-title">{t.title}</div>
                      <div className="row" style={{ marginTop: 8 }}>
                        {t.assigneeName && <span className="avatar" title={t.assigneeName}>{t.assigneeName[0]}</span>}
                        {t.isBlocked && <span className="badge error">Bloqueada</span>}
                        {isOverdue(t.dueDate) && <span className="badge error">Vencida</span>}
                        {t.checklistProgress !== null && t.checklistProgress !== undefined && (
                          <span className="muted small">✓ {t.checklistProgress}%</span>
                        )}
                        <span className="grow" />
                        {t.dueDate && <span className="muted small">{fmtDate(t.dueDate)}</span>}
                      </div>
                    </div>
                  ))}
                  {colItems.length === 0 && <div className="board-empty">Arrastra aquí</div>}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {showCreate && <TaskEditorPanel projectId={id!} states={states} onClose={() => setShowCreate(false)} onSaved={() => { setShowCreate(false); void load(); }} />}
    </div>
  );
}
