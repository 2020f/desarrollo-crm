import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import type { EpicDto } from '../types';
import { PRIORITY_LABELS, fmtDate } from '../constants';

export default function BacklogPage() {
  const { id } = useParams<{ id: string }>();
  const [epics, setEpics] = useState<EpicDto[]>([]);

  useEffect(() => {
    if (!id) return;
    api.get<EpicDto[]>(`/api/projects/${id}/epics`).then(setEpics).catch(() => {});
  }, [id]);

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Backlog</h1>
        <div className="grow" />
        <Link className="btn primary" to={`/projects/${id}/board`}>Tablero</Link>
      </div>
      <div className="card">
        {epics.map((e) => (
          <div key={e.id} style={{ padding: '10px 0', borderBottom: '1px solid var(--border)' }}>
            <div className="row">
              <span className="badge primary">Épica</span>
              <span style={{ fontWeight: 600 }}>{e.title}</span>
              <span className="grow" />
              <span className="badge">{PRIORITY_LABELS[e.priority]}</span>
              <span className="muted small">{fmtDate(e.dueDate)}</span>
            </div>
            {e.goal && <p className="muted small mt-8">{e.goal}</p>}
          </div>
        ))}
        {epics.length === 0 && <div className="empty">El backlog aún no tiene épicas. Las tareas se gestionan directamente en el tablero.</div>}
      </div>
    </div>
  );
}
