import { useEffect, useState, FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, qs } from '../api';
import type { Paged, TicketDto, MemberDto, ProjectDto } from '../types';
import { TICKET_STATUS_LABELS, TICKET_CATEGORY_LABELS, SEVERITY_LABELS, fmtDate } from '../constants';

export default function TicketsPage() {
  const [tickets, setTickets] = useState<TicketDto[]>([]);
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);

  const load = async () => {
    setLoading(true);
    const r = await api.get<Paged<TicketDto>>(`/api/tickets${qs({ status: status || undefined })}`);
    setTickets(r.items);
    setLoading(false);
  };

  useEffect(() => { load(); }, [status]);

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Solicitudes</h1>
        <div className="grow" />
        <button className="btn primary" onClick={() => setShowCreate(true)}>+ Nueva solicitud</button>
      </div>
      <div className="row" style={{ marginBottom: 16 }}>
        <select className="input" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Todos los estados</option>
          {TICKET_STATUS_LABELS.map((s, i) => <option key={s} value={i}>{s}</option>)}
        </select>
      </div>
      {loading ? <div className="empty">Cargando…</div> : (
        <div className="card">
          {tickets.map((t) => (
            <div key={t.id} className="row" style={{ padding: '10px 0', borderBottom: '1px solid var(--border)' }}>
              <Link className="muted small" to={`/tickets/${t.id}`}>{t.code}</Link>
              <Link className="grow" to={`/tickets/${t.id}`}>{t.title}</Link>
              <span className="pill">{TICKET_CATEGORY_LABELS[t.category]}</span>
              <span className="badge">{SEVERITY_LABELS[t.severity]}</span>
              <span className={`badge ${t.status === 5 || t.status === 6 ? 'success' : t.status === 0 ? 'warn' : ''}`}>{TICKET_STATUS_LABELS[t.status]}</span>
              {t.projectCode && <span className="pill">{t.projectCode}</span>}
              <span className="muted small">{fmtDate(t.createdAt)}</span>
            </div>
          ))}
          {tickets.length === 0 && <div className="empty">Sin solicitudes.</div>}
        </div>
      )}
      {showCreate && <CreateTicketModal onClose={() => setShowCreate(false)} onCreated={() => { setShowCreate(false); load(); }} />}
    </div>
  );
}

function CreateTicketModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [title, setTitle] = useState('');
  const [category, setCategory] = useState(0);
  const [severity, setSeverity] = useState(1);
  const [description, setDescription] = useState('');
  const [projectId, setProjectId] = useState('');
  const [assigneeId, setAssigneeId] = useState('');
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [error, setError] = useState('');

  useEffect(() => {
    api.get<MemberDto[]>('/api/organizations/members').then(setMembers).catch(() => {});
    api.get<Paged<ProjectDto>>('/api/projects').then((r) => setProjects(r.items)).catch(() => {});
  }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      await api.post('/api/tickets', {
        title, category, severity, description, priority: 1,
        projectId: projectId || null, assigneeId: assigneeId || null,
      });
      onCreated();
    } catch (err) { setError((err as Error).message); }
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal card" onClick={(e) => e.stopPropagation()}>
        <h3>Nueva solicitud</h3>
        {error && <div className="alert error">{error}</div>}
        <form onSubmit={submit}>
          <div className="field"><label>Título</label><input className="input" value={title} onChange={(e) => setTitle(e.target.value)} autoFocus required /></div>
          <div className="row">
            <div className="field grow">
              <label>Categoría</label>
              <select className="input" value={category} onChange={(e) => setCategory(Number(e.target.value))}>{TICKET_CATEGORY_LABELS.map((t, i) => <option key={t} value={i}>{t}</option>)}</select>
            </div>
            <div className="field grow">
              <label>Severidad</label>
              <select className="input" value={severity} onChange={(e) => setSeverity(Number(e.target.value))}>{SEVERITY_LABELS.map((t, i) => <option key={t} value={i}>{t}</option>)}</select>
            </div>
          </div>
          <div className="field"><label>Descripción</label><textarea className="input" value={description} onChange={(e) => setDescription(e.target.value)} /></div>
          <div className="row" style={{ alignItems: 'flex-start' }}>
            <div className="field grow">
              <label>Proyecto</label>
              <select className="input" value={projectId} onChange={(e) => setProjectId(e.target.value)}>
                <option value="">General (sin proyecto)</option>
                {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </div>
            <div className="field grow">
              <label>Asignar a</label>
              <select className="input" value={assigneeId} onChange={(e) => setAssigneeId(e.target.value)}>
                <option value="">General (sin asignar)</option>
                {members.map((m) => <option key={m.userId} value={m.userId}>{m.name}</option>)}
              </select>
            </div>
          </div>
          <div className="row" style={{ justifyContent: 'flex-end' }}>
            <button type="button" className="btn" onClick={onClose}>Cancelar</button>
            <button className="btn primary">Crear</button>
          </div>
        </form>
      </div>
    </div>
  );
}
