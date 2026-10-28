import { useEffect, useRef, useState, FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import type { TicketDto, TicketCommentDto, MemberDto } from '../types';
import { TICKET_STATUS_LABELS, TICKET_CATEGORY_LABELS, SEVERITY_LABELS, fmtDateTime, initials } from '../constants';

export default function TicketDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [ticket, setTicket] = useState<TicketDto | null>(null);
  const [comments, setComments] = useState<TicketCommentDto[]>([]);
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [error, setError] = useState('');
  const [actionError, setActionError] = useState('');
  const [busy, setBusy] = useState(false);
  const requestSequence = useRef(0);
  const activeTicketId = useRef(id);
  activeTicketId.current = id;

  const load = async () => {
    if (!id || activeTicketId.current !== id) return;
    const sequence = ++requestSequence.current;
    const current = () => sequence === requestSequence.current && activeTicketId.current === id;
    try {
      const [t, c] = await Promise.all([
        api.get<TicketDto>(`/api/tickets/${id}`),
        api.get<TicketCommentDto[]>(`/api/tickets/${id}/comments`),
      ]);
      if (!current()) return;
      setTicket(t); setComments(c); setMembers([]); setError('');
      if (t.canAssign) {
        const path = t.projectId ? `/api/projects/${t.projectId}/members` : '/api/organizations/members';
        api.get<MemberDto[]>(path).then((members) => { if (current()) setMembers(members.filter((member) => member.isActive)); }).catch(() => { if (current()) setMembers([]); });
      } else setMembers([]);
    } catch (e) { if (current()) setError((e as Error).message); }
  };

  useEffect(() => {
    setTicket(null); setComments([]); setMembers([]); setError(''); setActionError(''); setBusy(false);
    void load();
    return () => { requestSequence.current++; };
  }, [id]);
  const mutate = async (path: string, payload: unknown) => {
    if (busy || activeTicketId.current !== id || ticket?.id !== id) return;
    setBusy(true); setActionError('');
    try { await api.post(path, payload); if (activeTicketId.current === id) await load(); }
    catch (failure) { if (activeTicketId.current === id) setActionError((failure as Error).message); }
    finally { if (activeTicketId.current === id) setBusy(false); }
  };

  if (ticket && ticket.id !== id) return <div className="empty">Cargando…</div>;
  if (error) return <div className="alert error">{error}</div>;
  if (!ticket) return <div className="empty">Cargando…</div>;

  return (
    <div>
      <Link className="muted small" to="/tickets">← Solicitudes</Link>
      {actionError && <div className="alert error" role="alert">{actionError}</div>}
      <div className="row" style={{ margin: '8px 0 16px' }}>
        <div>
          <div className="muted small">{ticket.code} · {TICKET_CATEGORY_LABELS[ticket.category]}</div>
          <h1 className="page-title" style={{ margin: 0 }}>{ticket.title}</h1>
        </div>
        <div className="grow" />
        <span className={`badge ${ticket.status === 5 || ticket.status === 6 ? 'success' : ticket.status === 0 ? 'warn' : ''}`}>{TICKET_STATUS_LABELS[ticket.status]}</span>
      </div>

      <div className="two-col">
        <div>
          <section className="card" style={{ marginBottom: 16 }}>
            <div className="row">
              <span className="badge">Severidad: {SEVERITY_LABELS[ticket.severity]}</span>
              {ticket.environment && <span className="badge">Ambiente: {ticket.environment}</span>}
              {ticket.affectedVersion && <span className="badge">Versión: {ticket.affectedVersion}</span>}
              {ticket.requesterName && <span className="badge">Solicitante: {ticket.requesterName}</span>}
              <span className="grow" />
              <span className="muted small">{fmtDateTime(ticket.createdAt)}</span>
            </div>
          </section>

          <section className="card" style={{ marginBottom: 16 }}>
            <h3>Comentarios y notas</h3>
            {comments.map((c) => (
              <div key={c.id} style={{ padding: '8px 0', borderBottom: '1px solid var(--border)' }}>
                <div className="row">
                  <span className="avatar">{initials(c.authorName)}</span>
                  <span className="small" style={{ fontWeight: 600 }}>{c.authorName}</span>
                  {c.isInternal && <span className="badge warn">Nota interna</span>}
                  <span className="muted small">{fmtDateTime(c.createdAt)}</span>
                </div>
                <p style={{ margin: '6px 0 0' }}>{c.body}</p>
              </div>
            ))}
            {ticket.canComment ? <CommentForm key={ticket.id} ticketId={ticket.id} canWriteInternal={ticket.canWriteInternalNotes === true} onAdded={load} /> : <p className="muted small">Tienes acceso de solo lectura a esta solicitud.</p>}
          </section>
        </div>

        <div>
          {ticket.canAssign && <section className="card" style={{ marginBottom: 16 }}>
            <h3>Asignar a</h3>
            <select aria-label="Asignar solicitud" disabled={busy} className="input" style={{ width: '100%' }} value={ticket.assigneeId || ''} onChange={(e) => void mutate(`/api/tickets/${id}/assign`, { assigneeId: e.target.value || null })}>
              <option value="">General (sin asignar)</option>
              {members.map((m) => <option key={m.userId} value={m.userId}>{m.name}</option>)}
            </select>
          </section>}
          {ticket.canChangeStatus && <section className="card">
            <h3>Cambiar estado</h3>
            <div className="stack">
              {TICKET_STATUS_LABELS.map((s, i) => (
                <button key={s} disabled={busy} className="btn" style={{ justifyContent: 'flex-start' }} onClick={() => {
                  const reason = prompt(`Motivo para "${s}" (opcional)`) || undefined;
                  void mutate(`/api/tickets/${id}/status`, { status: i, reason });
                }}>{s}</button>
              ))}
            </div>
          </section>}
        </div>
      </div>
    </div>
  );
}

function CommentForm({ ticketId, canWriteInternal, onAdded }: { ticketId: string; canWriteInternal: boolean; onAdded: () => void }) {
  const [body, setBody] = useState('');
  const [internal, setInternal] = useState(false);
  const [error, setError] = useState('');
  const [sending, setSending] = useState(false);
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!body.trim() || sending) return;
    setSending(true); setError('');
    try {
      await api.post(`/api/tickets/${ticketId}/comments`, { body, isInternal: canWriteInternal && internal });
      setBody(''); setInternal(false); onAdded();
    } catch (failure) { setError((failure as Error).message); }
    finally { setSending(false); }
  };
  return (
    <form onSubmit={submit} className="mt-16">
      {error && <div className="alert error" role="alert">{error}</div>}
      <textarea aria-label="Comentario de la solicitud" disabled={sending} className="input" style={{ width: '100%' }} placeholder="Respuesta o nota…" value={body} onChange={(e) => setBody(e.target.value)} />
      <div className="row mt-8">
        {canWriteInternal && <label className="row small muted"><input type="checkbox" disabled={sending} checked={internal} onChange={(e) => setInternal(e.target.checked)} /> Nota interna</label>}
        <button className="btn primary sm" disabled={sending}>{sending ? 'Enviando…' : 'Enviar'}</button>
      </div>
    </form>
  );
}
