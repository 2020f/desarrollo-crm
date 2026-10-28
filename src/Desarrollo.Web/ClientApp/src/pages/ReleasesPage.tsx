import { useEffect, useState } from 'react';
import { api } from '../api';
import type { Paged, ProjectDto, ReleaseDto } from '../types';
import { fmtDate } from '../constants';

export default function ReleasesPage() {
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [projectId, setProjectId] = useState('');
  const [releases, setReleases] = useState<ReleaseDto[]>([]);

  useEffect(() => {
    api.get<Paged<ProjectDto>>('/api/projects').then((r) => { setProjects(r.items); if (r.items.length) setProjectId(r.items[0].id); });
  }, []);

  useEffect(() => {
    if (!projectId) return;
    api.get<ReleaseDto[]>(`/api/projects/${projectId}/releases`).then(setReleases);
  }, [projectId]);

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Entregas</h1>
        <div className="grow" />
        <select className="input" value={projectId} onChange={(e) => setProjectId(e.target.value)}>
          {projects.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
        </select>
      </div>
      <div className="card">
        {releases.map((r) => (
          <div key={r.id} style={{ padding: '12px 0', borderBottom: '1px solid var(--border)' }}>
            <div className="row">
              <span style={{ fontWeight: 600 }}>{r.name}</span>
              {r.releasedAt ? <span className="badge success">Publicada</span> : <span className="badge warn">Pendiente</span>}
              <span className="grow" />
              <span className="muted small">Prevista: {fmtDate(r.plannedDate)}</span>
            </div>
            {r.environments && <div className="muted small mt-8">Ambientes: {r.environments}</div>}
            {r.releaseNotes && <p className="muted small">{r.releaseNotes}</p>}
          </div>
        ))}
        {releases.length === 0 && <div className="empty">Sin entregas registradas.</div>}
      </div>
    </div>
  );
}
