import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, qs } from '../api';
import type { Paged, WorkItemCardDto, WorkflowStateDto, TaskDiagramDto } from '../types';
import { PRIORITY_LABELS, TYPE_LABELS, fmtDate } from '../constants';

export default function TablePage() {
  const { id } = useParams<{ id: string }>();
  const [states, setStates] = useState<WorkflowStateDto[]>([]);
  const [items, setItems] = useState<WorkItemCardDto[]>([]);
  const [assigneeOnly, setAssigneeOnly] = useState(false);
  const [error, setError] = useState('');
  const [permissions, setPermissions] = useState<number[]>([]);
  const [pending, setPending] = useState<string | null>(null);
  const requestSequence = useRef(0);
  const activeProjectId = useRef(id);
  const changingState = useRef(false);
  activeProjectId.current = id;

  const load = async () => {
    if (!id) return;
    const sequence = ++requestSequence.current;
    const current = () => sequence === requestSequence.current && activeProjectId.current === id;
    try {
    const [s, w, diagram] = await Promise.all([
      api.get<WorkflowStateDto[]>(`/api/projects/${id}/states`),
      api.get<Paged<WorkItemCardDto>>(`/api/workitems${qs({ projectId: id, pageSize: 500 })}`),
      api.get<TaskDiagramDto>(`/api/projects/${id}/task-diagram`),
    ]);
    if (!current()) return;
    setStates(s);
    setItems(w.items);
    setPermissions(diagram.permissions); setError('');
    } catch (e) { if (current()) setError((e as Error).message); }
  };

  useEffect(() => { setItems([]); setPermissions([]); void load(); return () => { requestSequence.current++; }; }, [id]);

  const shown = assigneeOnly ? items.filter((i) => i.assigneeId) : items;

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Tabla</h1>
        <div className="grow" />
        <Link className="btn" to={`/projects/${id}/board`}>Tablero</Link>
        <Link className="btn" to={`/projects/${id}/diagram`}>Diagrama</Link>
        <label className="row small muted"><input type="checkbox" checked={assigneeOnly} onChange={(e) => setAssigneeOnly(e.target.checked)} /> Solo con responsable</label>
      </div>
      {error && <div className="alert error" role="alert">{error} <button className="btn sm" onClick={() => void load()}>Actualizar datos</button></div>}
      <div className="card table-card">
        <table className="data-table">
          <thead><tr><th>Código</th><th>Título</th><th>Tipo</th><th>Prioridad</th><th>Responsable</th><th>Estado</th><th>Vence</th></tr></thead>
          <tbody>
            {shown.map((t) => (
              <tr key={t.id}>
                <td className="muted small">{t.code}</td>
                <td><Link to={`/tasks/${t.id}`}>{t.title}</Link></td>
                <td className="small">{TYPE_LABELS[t.type]}</td>
                <td><span className={`badge ${t.priority >= 2 ? 'error' : ''}`}>{PRIORITY_LABELS[t.priority]}</span></td>
                <td className="small">{t.assigneeName || <span className="muted">—</span>}</td>
                <td>
                  <select aria-label={`Estado de ${t.code}`} className="input sm" value={t.stateId} disabled={!permissions.includes(5) || pending !== null} onChange={async (e) => {
                    if (changingState.current) return;
                    changingState.current = true;
                    setPending(t.id); setError('');
                    try { await api.post(`/api/workitems/${t.id}/transition`, { toStateId: e.target.value, rowVersion: t.rowVersion }); await load(); }
                    catch (err) { setError((err as Error).message); } finally { setPending(null); changingState.current = false; }
                  }}>
                    {states.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
                  </select>
                </td>
                <td className="small">{fmtDate(t.dueDate) || '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {shown.length === 0 && <div className="empty">Sin tareas.</div>}
      </div>
    </div>
  );
}
