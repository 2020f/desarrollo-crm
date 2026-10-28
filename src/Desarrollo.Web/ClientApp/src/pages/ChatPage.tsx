import { useCallback, useEffect, useState, FormEvent, useRef } from 'react';
import { api } from '../api';
import { useAuth } from '../App';
import type { ChannelDto, MessageDto, MemberDto, Paged } from '../types';
import { fmtDateTime, initials } from '../constants';
import { useLiveActivity } from '../live/LiveActivityProvider';

const CHAT_COLORS = ['#9B8AFB', '#57D6C7', '#EAC385', '#E0788A', '#7C9CF5', '#4FB3D9', '#B48CF2', '#5FC48A', '#F0A06B', '#E86E9A', '#5AA9E6', '#8AD7C1'];

function colorFor(id: string): string {
  let h = 0;
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0;
  return CHAT_COLORS[h % CHAT_COLORS.length];
}

export default function ChatPage() {
  const { user } = useAuth();
  const { snapshot, chatVersion, accessLost, connected, refresh } = useLiveActivity();
  const [channels, setChannels] = useState<ChannelDto[]>([]);
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [active, setActive] = useState<ChannelDto | null>(null);
  const [messages, setMessages] = useState<MessageDto[]>([]);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const body = active ? drafts[active.id] || '' : '';
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [sending, setSending] = useState(false);
  const [messageChannelId, setMessageChannelId] = useState('');
  const mounted = useRef(true), sendingRef = useRef(false), messageSequence = useRef(0), channelSequence = useRef(0);
  const accessEpoch = useRef(0), accessLostRef = useRef(accessLost); accessLostRef.current = accessLost;
  const activeId = useRef(active?.id); activeId.current = active?.id;
  const endRef = useRef<HTMLDivElement>(null);

  useEffect(() => { mounted.current = true; return () => { mounted.current = false; messageSequence.current++; channelSequence.current++; }; }, []);

  const loadChannels = useCallback(async () => {
    const sequence = ++channelSequence.current;
    try {
      const r = await api.get<ChannelDto[]>('/api/channels');
      if (!mounted.current || accessLostRef.current || sequence !== channelSequence.current) return;
      setChannels(r);
      setActive((prev) => r.find((c) => c.id === prev?.id) ?? r[0] ?? null);
    } catch (failure) {
      if (!mounted.current || sequence !== channelSequence.current) return;
      setError('No se pudieron cargar las conversaciones: ' + (failure as Error).message);
      const status = (failure as Error & { status?: number }).status;
      if (status === 401 || status === 403) { setChannels([]); setMembers([]); setActive(null); setMessages([]); }
    }
  }, []);

  const loadMessages = useCallback(async (channelId: string) => {
    const sequence = ++messageSequence.current;
    setLoading(true);
    try {
      const r = await api.get<Paged<MessageDto>>(`/api/channels/${channelId}/messages`);
      if (!mounted.current || activeId.current !== channelId || sequence !== messageSequence.current) return;
      setMessages(r.items); setMessageChannelId(channelId); setError('');
      if (document.visibilityState === 'visible') {
        await api.post(`/api/channels/${channelId}/read`, {}).catch(() => {});
        if (mounted.current && activeId.current === channelId) void refresh();
      }
    } catch (failure) {
      if (!mounted.current || activeId.current !== channelId || sequence !== messageSequence.current) return;
      setError('No se pudieron actualizar los mensajes: ' + (failure as Error).message);
      const status = (failure as Error & { status?: number }).status;
      if (status === 401 || status === 403 || status === 404) { setMessages([]); setMessageChannelId(''); void loadChannels(); }
    } finally { if (mounted.current && activeId.current === channelId && sequence === messageSequence.current) setLoading(false); }
  }, [loadChannels, refresh]);

  useEffect(() => {
    let current = true;
    if (accessLost) { setMembers([]); return; }
    api.get<MemberDto[]>('/api/organizations/members').then((r) => { if (current) setMembers(r.filter((m) => m.userId !== user?.id && m.isActive)); })
      .catch(() => { if (current) setMembers([]); });
    return () => { current = false; };
  }, [user?.id, accessLost]);
  useEffect(() => {
    if (accessLost) { accessEpoch.current++; channelSequence.current++; messageSequence.current++; setChannels([]); setMembers([]); setActive(null); setMessages([]); return; }
    void loadChannels();
  }, [chatVersion, accessLost, loadChannels]);
  useEffect(() => {
    if (active && !accessLost) void loadMessages(active.id);
    return () => { messageSequence.current++; };
  }, [active?.id, chatVersion, accessLost, connected, loadMessages]);
  useEffect(() => { endRef.current?.scrollIntoView?.({ behavior: 'smooth' }); }, [messages]);

  const openDirect = async (userId: string) => {
    if (accessLostRef.current) return;
    const epoch = accessEpoch.current;
    try {
      const c = await api.post<ChannelDto>('/api/channels/direct', { userId });
      if (!mounted.current || accessLostRef.current || epoch !== accessEpoch.current) return;
      channelSequence.current++;
      setChannels((prev) => prev.some((x) => x.id === c.id) ? prev : [...prev, c]);
      setActive(c); setError(''); void refresh();
    } catch (failure) { if (mounted.current) setError('No se pudo abrir el chat: ' + (failure as Error).message); }
  };

  const send = async (e: FormEvent) => {
    e.preventDefault();
    if (!active || !body.trim() || sendingRef.current || accessLost) return;
    const channelId = active.id, submitted = body;
    sendingRef.current = true; setSending(true); setError('');
    try {
      await api.post(`/api/channels/${channelId}/messages`, { body: submitted });
      if (!mounted.current) return;
      setDrafts((previous) => previous[channelId] === submitted ? { ...previous, [channelId]: '' } : previous);
      if (activeId.current === channelId) await loadMessages(channelId);
      void refresh();
    } catch (failure) { if (mounted.current) setError('No se pudo enviar el mensaje. Conservamos tu texto: ' + (failure as Error).message); }
    finally { sendingRef.current = false; if (mounted.current) setSending(false); }
  };

  const projectChannels = channels.filter((c) => c.type === 0);
  const savedDirects = channels.filter((c) => c.type === 1);
  const unreadFor = (id: string) => snapshot?.channels.find((c) => c.id === id)?.unreadCount ?? 0;
  const visibleMessages = messageChannelId === active?.id ? messages : [];

  return (
    <div className="chat-layout">
      <aside className="card chat-side">
        <h3 style={{ padding: '4px 6px' }}>Mensajes directos</h3>
        {savedDirects.map((c) => <button key={c.id} className={`nav-item ${active?.id === c.id ? 'active' : ''}`} style={{ width: '100%', textAlign: 'left', background: 'transparent', border: 'none' }} onClick={() => setActive(c)}><span className="avatar">{initials(c.name)}</span><span>{c.name}</span>{unreadFor(c.id) > 0 && <span className="badge">{unreadFor(c.id)}</span>}</button>)}
        {members.filter((m) => !savedDirects.some((c) => c.directUserId === m.userId)).map((m) => (
          <button key={m.userId} className="nav-item" style={{ width: '100%', textAlign: 'left', background: 'transparent', border: 'none' }} onClick={() => openDirect(m.userId)}>
            <span className="avatar">{initials(m.name)}</span> <span>{m.name}</span>
          </button>
        ))}
        {members.length === 0 && savedDirects.length === 0 && <div className="muted small" style={{ padding: 8 }}>No hay otros miembros.</div>}

        <h3 style={{ padding: '10px 6px 4px' }}>Canales</h3>
        {projectChannels.map((c) => (
          <button key={c.id} className={`nav-item ${active?.id === c.id ? 'active' : ''}`} style={{ width: '100%', textAlign: 'left', background: 'transparent', border: 'none' }} onClick={() => setActive(c)}>
            # {c.name} {c.projectCode && <span className="muted small">({c.projectCode})</span>}{unreadFor(c.id) > 0 && <span className="badge">{unreadFor(c.id)}</span>}
          </button>
        ))}
        {projectChannels.length === 0 && <div className="muted small" style={{ padding: 8 }}>Sin canales.</div>}
      </aside>

      <section className="card chat-main">
        <div className="chat-head row">
          {active?.type === 1 && <span className="avatar">{initials(active.name)}</span>}
          <span className="grow">{active ? (active.type === 1 ? active.name : `# ${active.name}`) : 'Selecciona una conversación'}</span>
        </div>
        {error && <div className="alert error" role="alert">{error}<button className="btn sm" onClick={() => { void loadChannels(); if (active) void loadMessages(active.id); void refresh(); }}>Reintentar</button></div>}
        <div className="chat-body" role="log" aria-label="Mensajes de la conversación" aria-live="polite" aria-relevant="additions text">
          {loading && visibleMessages.length === 0 && active && <div className="empty" role="status">Cargando mensajes…</div>}
          {!loading && active && visibleMessages.length === 0 && !error && <div className="empty">Todavía no hay mensajes en esta conversación.</div>}
          {visibleMessages.map((m) => {
            const isMine = m.authorId === user?.id;
            const c = colorFor(m.authorId);
            return (
              <div key={m.id} className={`chat-msg ${isMine ? 'mine' : ''}`}>
                {!isMine && <span className="avatar" style={{ background: c, color: '#0B1020' }}>{initials(m.authorName)}</span>}
                <div className={`bubble ${isMine ? 'mine' : ''}`} style={isMine ? undefined : { borderLeftColor: c, background: `${c}22`, borderColor: `${c}55` }}>
                  <div className="row">
                    <span style={{ fontWeight: 600, color: isMine ? undefined : c }}>{isMine ? 'Tú' : (m.authorName || 'Usuario')}</span>
                    <span className="muted small">{fmtDateTime(m.createdAt)}</span>
                    {m.isEdited && <span className="muted small">(editado)</span>}
                  </div>
                  <p style={{ margin: '2px 0 0', whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{m.body}</p>
                </div>
              </div>
            );
          })}
          <div ref={endRef} />
        </div>
        <form className="chat-input row" onSubmit={send}>
          <input className="input grow" placeholder="Escribe un mensaje…" aria-label="Mensaje" value={body} disabled={!active || accessLost} maxLength={20000} onChange={(e) => { if (active) setDrafts((previous) => ({ ...previous, [active.id]: e.target.value })); }} />
          <button className="btn primary" disabled={!active || !body.trim() || sending || accessLost}>{sending ? 'Enviando…' : 'Enviar'}</button>
        </form>
      </section>
    </div>
  );
}
