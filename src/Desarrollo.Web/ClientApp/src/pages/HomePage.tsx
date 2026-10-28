import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, qs } from '../api';
import { useAuth } from '../App';
import type { Paged, ProjectDto, WorkItemCardDto } from '../types';
import { fmtDate, PRIORITY_LABELS, PRIORITY_CLASS, isOverdue } from '../constants';

export default function HomePage() {
  const { user } = useAuth();
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [mine, setMine] = useState<WorkItemCardDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      setLoading(true);
      const [p, w] = await Promise.all([
        api.get<Paged<ProjectDto>>('/api/projects'),
        user?.id ? api.get<Paged<WorkItemCardDto>>(`/api/workitems${qs({ assigneeId: user.id, pageSize: 100 })}`) : Promise.resolve({ items: [], total: 0, page: 1, pageSize: 100 } as Paged<WorkItemCardDto>),
      ]);
      setProjects(p.items);
      setMine(w.items);
      setLoading(false);
    })();
  }, [user?.id]);

  const open = mine.filter((i) => !i.stateName || !['Publicada', 'Aceptada'].includes(i.stateName));
  const overdue = open.filter((i) => isOverdue(i.dueDate));
  const blocked = open.filter((i) => i.isBlocked);

  return (
    <div>
      <h1 className="page-title">Inicio</h1>
      {loading ? <div className="empty">Cargando…</div> : (
        <>
          <div className="stat-grid">
            <Stat label="Mis tareas abiertas" value={open.length} to="/my-work" />
            <Stat label="Vencidas" value={overdue.length} tone="warn" to="/my-work" />
            <Stat label="Bloqueadas" value={blocked.length} tone="error" to="/my-work" />
            <Stat label="Proyectos" value={projects.length} to="/projects" />
          </div>

          <div className="two-col">
            <section className="card">
              <h3>Mis prioridades</h3>
              {open.length === 0 && <div className="empty">Sin tareas pendientes.</div>}
              {open.slice(0, 8).map((t) => (
                <WorkRow key={t.id} t={t} />
              ))}
            </section>

            <section className="card">
              <h3>Proyectos</h3>
              {projects.length === 0 && <div className="empty"><Link to="/projects">Crear proyecto</Link></div>}
              {projects.slice(0, 8).map((p) => (
                <div key={p.id} className="row" style={{ padding: '8px 0', borderBottom: '1px solid var(--border)' }}>
                  <span className="pill">{p.code}</span>
                  <Link className="grow" to={`/projects/${p.id}`}>{p.name}</Link>
                  <span className="muted small">{fmtDate(p.dueDate)}</span>
                </div>
              ))}
            </section>
          </div>
        </>
      )}
    </div>
  );
}

function Stat({ label, value, tone, to }: { label: string; value: number; tone?: string; to: string }) {
  return (
    <Link to={to} className="stat card">
      <div className="stat-value" style={{ color: tone === 'warn' ? 'var(--warn)' : tone === 'error' ? 'var(--error)' : 'var(--text)' }}>{value}</div>
      <div className="muted small">{label}</div>
    </Link>
  );
}

function WorkRow({ t }: { t: WorkItemCardDto }) {
  const pclass = PRIORITY_CLASS[t.priority] || '';
  return (
    <div className="row" style={{ padding: '8px 0', borderBottom: '1px solid var(--border)' }}>
      <span className={`badge ${pclass}`}>{PRIORITY_LABELS[t.priority]}</span>
      <Link className="grow" to={`/tasks/${t.id}`}>{t.code} — {t.title}</Link>
      {t.isBlocked && <span className="badge error">Bloqueada</span>}
      {isOverdue(t.dueDate) && <span className="badge error">Vencida</span>}
      {t.stateName && <span className="muted small">{t.stateName}</span>}
    </div>
  );
}
