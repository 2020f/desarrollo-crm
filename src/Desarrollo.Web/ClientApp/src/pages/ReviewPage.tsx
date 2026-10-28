import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import type { Paged, WorkItemCardDto } from '../types';
import { fmtDate } from '../constants';

const REVIEW_STATES = ['En revisión', 'En pruebas'];

export default function ReviewPage() {
  const [items, setItems] = useState<WorkItemCardDto[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.get<Paged<WorkItemCardDto>>('/api/workitems?pageSize=500').then((r) => {
      setItems(r.items.filter((i) => REVIEW_STATES.includes(i.stateName)));
      setLoading(false);
    });
  }, []);

  return (
    <div>
      <h1 className="page-title">Revisión y QA</h1>
      <div className="card">
        {loading ? <div className="empty">Cargando…</div> : items.map((t) => (
          <div key={t.id} className="row" style={{ padding: '10px 0', borderBottom: '1px solid var(--border)' }}>
            <span className="badge primary">{t.stateName}</span>
            <Link className="grow" to={`/tasks/${t.id}`}>{t.code} — {t.title}</Link>
            {t.projectCode && <span className="pill">{t.projectCode}</span>}
            {t.assigneeName && <span className="muted small">{t.assigneeName}</span>}
            <span className="muted small">{fmtDate(t.dueDate)}</span>
          </div>
        ))}
        {!loading && items.length === 0 && <div className="empty">Cola de revisión vacía.</div>}
      </div>
    </div>
  );
}
