import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, qs } from '../api';
import { useAuth } from '../App';
import type { Paged, WorkItemCardDto } from '../types';
import { PRIORITY_LABELS, PRIORITY_CLASS, fmtDate, isOverdue } from '../constants';

export default function MyWorkPage() {
  const { user } = useAuth();
  const [tab, setTab] = useState<'assigned' | 'created' | 'overdue'>('assigned');
  const [items, setItems] = useState<WorkItemCardDto[]>([]);
  const [loading, setLoading] = useState(true);

  const load = async () => {
    setLoading(true);
    let url: string;
    if (tab === 'assigned') url = `/api/workitems${qs({ assigneeId: user?.id, pageSize: 200 })}`;
    else if (tab === 'overdue') url = `/api/workitems${qs({ overdue: true, pageSize: 200 })}`;
    else url = `/api/workitems${qs({ pageSize: 200 })}`;
    const r = await api.get<Paged<WorkItemCardDto>>(url);
    let list = r.items;
    if (tab === 'created') list = list.filter((i) => (i as unknown as { authorId?: string }).authorId === user?.id);
    setItems(list);
    setLoading(false);
  };

  useEffect(() => { load(); }, [tab, user?.id]);

  return (
    <div>
      <h1 className="page-title">Mi trabajo</h1>
      <div className="tabs">
        {([['assigned', 'Asignadas'], ['created', 'Creadas'], ['overdue', 'Vencidas']] as const).map(([k, label]) => (
          <button key={k} className={`tab ${tab === k ? 'active' : ''}`} onClick={() => setTab(k)}>{label}</button>
        ))}
      </div>
      {loading ? <div className="empty">Cargando…</div> : (
        <div className="card mt-16">
          {items.map((t) => (
            <div key={t.id} className="row" style={{ padding: '10px 0', borderBottom: '1px solid var(--border)' }}>
              <span className={`badge ${PRIORITY_CLASS[t.priority]}`}>{PRIORITY_LABELS[t.priority]}</span>
              <Link className="grow" to={`/tasks/${t.id}`}>{t.code} — {t.title}</Link>
              {t.projectCode && <span className="pill">{t.projectCode}</span>}
              <span className="muted small">{t.stateName}</span>
              {t.isBlocked && <span className="badge error">Bloqueada</span>}
              {isOverdue(t.dueDate) && <span className="badge error">Vencida</span>}
              <span className="muted small">{fmtDate(t.dueDate)}</span>
            </div>
          ))}
          {items.length === 0 && <div className="empty">No hay trabajo en esta vista.</div>}
        </div>
      )}
    </div>
  );
}
