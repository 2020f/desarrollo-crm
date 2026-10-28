import { useEffect, useState, FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api, qs } from '../api';
import type { Paged, ProjectDto } from '../types';
import { PROJECT_STATUS_LABELS, PRIORITY_LABELS, fmtDate } from '../constants';

export default function ProjectsPage() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [showCreate, setShowCreate] = useState(false);

  const load = async () => {
    setLoading(true);
    const r = await api.get<Paged<ProjectDto>>(`/api/projects${qs({ search, status: status || undefined })}`);
    setProjects(r.items);
    setLoading(false);
  };

  useEffect(() => { load(); }, [status]);

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Proyectos</h1>
        <div className="grow" />
        <button className="btn primary" onClick={() => setShowCreate(true)}>+ Nuevo proyecto</button>
      </div>

      <div className="row" style={{ marginBottom: 16 }}>
        <input className="input" placeholder="Buscar…" value={search} onChange={(e) => setSearch(e.target.value)} onKeyDown={(e) => e.key === 'Enter' && load()} />
        <select className="input" value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="">Todos los estados</option>
          {PROJECT_STATUS_LABELS.map((s, i) => <option key={s} value={i}>{s}</option>)}
        </select>
        <button className="btn" onClick={load}>Filtrar</button>
      </div>

      {loading ? <div className="empty">Cargando…</div> : (
        <div className="project-grid">
          {projects.map((p) => (
            <Link key={p.id} to={`/projects/${p.id}`} className="card project-card">
              <div className="row">
                <span className="pill">{p.code}</span>
                <span className={`badge ${p.status === 1 ? 'success' : ''}`}>{PROJECT_STATUS_LABELS[p.status]}</span>
                <span className="grow" />
                <span className="badge">{PRIORITY_LABELS[p.priority]}</span>
              </div>
              <h3 style={{ marginTop: 10 }}>{p.name}</h3>
              {p.objective && <p className="muted small">{p.objective}</p>}
              <div className="muted small mt-8">{fmtDate(p.dueDate) ? `Entrega: ${fmtDate(p.dueDate)}` : 'Sin fecha de entrega'}</div>
            </Link>
          ))}
          {projects.length === 0 && <div className="empty" style={{ gridColumn: '1 / -1' }}>No hay proyectos. Crea el primero.</div>}
        </div>
      )}

      {showCreate && <CreateProjectModal onClose={() => setShowCreate(false)} onCreated={() => { setShowCreate(false); load(); }} />}
    </div>
  );
}

function CreateProjectModal({ onClose, onCreated }: { onClose: () => void; onCreated: () => void }) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [objective, setObjective] = useState('');
  const [error, setError] = useState('');

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      await api.post('/api/projects', { name, code, objective });
      onCreated();
    } catch (err) { setError((err as Error).message); }
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal card" onClick={(e) => e.stopPropagation()}>
        <h3>Nuevo proyecto</h3>
        {error && <div className="alert error">{error}</div>}
        <form onSubmit={submit}>
          <div className="field"><label>Nombre</label><input className="input" value={name} onChange={(e) => setName(e.target.value)} required /></div>
          <div className="field"><label>Código (ej. GOGO)</label><input className="input" value={code} onChange={(e) => setCode(e.target.value)} required /></div>
          <div className="field"><label>Objetivo</label><textarea className="input" value={objective} onChange={(e) => setObjective(e.target.value)} /></div>
          <div className="row" style={{ justifyContent: 'flex-end' }}>
            <button type="button" className="btn" onClick={onClose}>Cancelar</button>
            <button className="btn primary">Crear</button>
          </div>
        </form>
      </div>
    </div>
  );
}
