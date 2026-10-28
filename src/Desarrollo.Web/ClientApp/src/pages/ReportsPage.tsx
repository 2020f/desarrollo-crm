import { useEffect, useState } from 'react';
import { api } from '../api';
import type { Paged, ProjectDto, ReportSnapshotDto } from '../types';

export default function ReportsPage() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [projectId, setProjectId] = useState('');
  const [report, setReport] = useState<ReportSnapshotDto | null>(null);

  useEffect(() => {
    api.get<Paged<ProjectDto>>('/api/projects').then((r) => { setProjects(r.items); if (r.items.length) setProjectId(r.items[0].id); });
  }, []);

  useEffect(() => {
    if (!projectId) return;
    api.get<ReportSnapshotDto>(`/api/reports/project/${projectId}`).then(setReport).catch(() => setReport(null));
  }, [projectId]);

  if (!report) return <div className="empty">Selecciona un proyecto.</div>;

  const max = Math.max(1, ...report.byState.map((s) => s.count));

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Reportes</h1>
        <div className="grow" />
        <select className="input" value={projectId} onChange={(e) => setProjectId(e.target.value)}>
          {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>
      </div>
      <div className="stat-grid">
        <Metric label="Tareas totales" value={report.totalTasks} />
        <Metric label="Finalizadas" value={report.doneTasks} />
        <Metric label="Vencidas" value={report.overdueTasks} tone="warn" />
        <Metric label="Bloqueadas" value={report.blockedTasks} tone="error" />
        <Metric label="Solicitudes abiertas" value={report.openTickets} />
        <Metric label="En curso (WIP)" value={report.wipLimit} />
      </div>
      <div className="card mt-16">
        <h3>Por estado</h3>
        {report.byState.map((s) => (
          <div key={s.key} style={{ marginBottom: 10 }}>
            <div className="row">
              <span className="small">{s.label}</span>
              <span className="grow" />
              <span className="muted small">{s.count}</span>
            </div>
            <div className="progress"><div className="progress-fill" style={{ width: `${(s.count / max) * 100}%` }} /></div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Metric({ label, value, tone }: { label: string; value: number; tone?: string }) {
  return (
    <div className="card">
      <div className="stat-value" style={{ color: tone === 'warn' ? 'var(--warn)' : tone === 'error' ? 'var(--error)' : 'var(--text)' }}>{value}</div>
      <div className="muted small">{label}</div>
    </div>
  );
}
