import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import type { Paged, NotificationDto } from '../types';
import { NOTIF_EVENT_LABELS, fmtDateTime } from '../constants';
import { useLiveActivity } from '../live/LiveActivityProvider';

export default function NotificationsPage() {
  const { snapshot, notificationVersion, accessLost, connected, refresh } = useLiveActivity();
  const [items, setItems] = useState<NotificationDto[]>([]);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const sequence = useRef(0), mounted = useRef(true);
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; sequence.current++; }; }, []);

  const load = useCallback(async () => {
    if (accessLost) { setItems([]); setLoading(false); return; }
    const request = ++sequence.current;
    setLoading(true);
    try {
      const r = await api.get<Paged<NotificationDto>>(`/api/notifications?unreadOnly=${unreadOnly}`);
      if (mounted.current && request === sequence.current) { setItems(r.items); setError(''); }
    } catch (failure) {
      if (mounted.current && request === sequence.current) { setItems([]); setError('No se pudieron actualizar las notificaciones: ' + (failure as Error).message); }
    } finally { if (mounted.current && request === sequence.current) setLoading(false); }
  }, [unreadOnly, accessLost]);

  useEffect(() => { void load(); return () => { sequence.current++; }; }, [load, notificationVersion, connected, snapshot?.observedAt]);

  const markRead = async (id?: string) => {
    try {
      await api.post(id ? `/api/notifications/${id}/read` : '/api/notifications/read-all', {});
      if (mounted.current) { await load(); void refresh(); }
    } catch (failure) { if (mounted.current) setError('No se pudo marcar como leído: ' + (failure as Error).message); }
  };

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Notificaciones</h1>
        <div className="grow" />
        <label className="row small muted"><input type="checkbox" checked={unreadOnly} onChange={(e) => setUnreadOnly(e.target.checked)} /> Solo no leídas</label>
        <button className="btn sm" disabled={accessLost} onClick={() => void markRead()}>Marcar todo leído</button>
      </div>
      <div className="card">
        {error && <div className="alert error" role="alert">{error}<button className="btn sm" onClick={() => { void load(); void refresh(); }}>Reintentar</button></div>}
        {loading ? <div className="empty">Cargando…</div> : items.map((n) => (
          <div key={n.id} className="row" style={{ padding: '10px 0', borderBottom: '1px solid var(--border)', opacity: n.isRead ? 0.6 : 1 }}>
            <span className="badge">{NOTIF_EVENT_LABELS[n.event] ?? 'Aviso'}</span>
            <div className="grow">
              <div>{n.message}</div>
              <div className="muted small">{fmtDateTime(n.createdAt)}</div>
            </div>
            {n.route && <Link className="btn sm" to={n.route} onClick={() => void markRead(n.id)}>Ver</Link>}
            {!n.isRead && <button className="btn ghost sm" aria-label="Marcar notificación como leída" onClick={() => void markRead(n.id)}>✓</button>}
          </div>
        ))}
        {items.length === 0 && !loading && <div className="empty">Sin notificaciones.</div>}
      </div>
    </div>
  );
}
