import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import type { Paged, NotificationDto } from '../types';
import { NOTIF_EVENT_LABELS, fmtDateTime } from '../constants';

export default function NotificationsPage() {
  const [items, setItems] = useState<NotificationDto[]>([]);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [loading, setLoading] = useState(true);

  const load = async () => {
    setLoading(true);
    const r = await api.get<Paged<NotificationDto>>(`/api/notifications?unreadOnly=${unreadOnly}`);
    setItems(r.items);
    setLoading(false);
  };

  useEffect(() => { load(); }, [unreadOnly]);

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Notificaciones</h1>
        <div className="grow" />
        <label className="row small muted"><input type="checkbox" checked={unreadOnly} onChange={(e) => setUnreadOnly(e.target.checked)} /> Solo no leídas</label>
        <button className="btn sm" onClick={async () => { await api.post('/api/notifications/read-all', {}); load(); }}>Marcar todo leído</button>
      </div>
      <div className="card">
        {loading ? <div className="empty">Cargando…</div> : items.map((n) => (
          <div key={n.id} className="row" style={{ padding: '10px 0', borderBottom: '1px solid var(--border)', opacity: n.isRead ? 0.6 : 1 }}>
            <span className="badge">{NOTIF_EVENT_LABELS[n.event] ?? 'Aviso'}</span>
            <div className="grow">
              <div>{n.message}</div>
              <div className="muted small">{fmtDateTime(n.createdAt)}</div>
            </div>
            {n.route && <Link className="btn sm" to={n.route} onClick={async () => { await api.post(`/api/notifications/${n.id}/read`, {}); }}>Ver</Link>}
            {!n.isRead && <button className="btn ghost sm" onClick={async () => { await api.post(`/api/notifications/${n.id}/read`, {}); load(); }}>✓</button>}
          </div>
        ))}
        {items.length === 0 && !loading && <div className="empty">Sin notificaciones.</div>}
      </div>
    </div>
  );
}
