import { useEffect, useState, FormEvent, useRef } from 'react';
import { api } from '../api';
import { useAuth } from '../App';
import type { ChannelDto, MessageDto, MemberDto, Paged } from '../types';
import { fmtDateTime, initials } from '../constants';

const CHAT_COLORS = ['#9B8AFB', '#57D6C7', '#EAC385', '#E0788A', '#7C9CF5', '#4FB3D9', '#B48CF2', '#5FC48A', '#F0A06B', '#E86E9A', '#5AA9E6', '#8AD7C1'];

function colorFor(id: string): string {
  let h = 0;
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0;
  return CHAT_COLORS[h % CHAT_COLORS.length];
}

export default function ChatPage() {
  const { user } = useAuth();
  const [channels, setChannels] = useState<ChannelDto[]>([]);
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [active, setActive] = useState<ChannelDto | null>(null);
  const [messages, setMessages] = useState<MessageDto[]>([]);
  const [body, setBody] = useState('');
  const endRef = useRef<HTMLDivElement>(null);

  const loadChannels = async () => {
    const r = await api.get<ChannelDto[]>('/api/channels');
    setChannels(r);
    setActive((prev) => prev && r.find((c) => c.id === prev.id) ? prev : (r[0] ?? null));
  };

  const loadMembers = async () => {
    const r = await api.get<MemberDto[]>('/api/organizations/members');
    setMembers(r.filter((m) => m.userId !== user?.id));
  };

  const loadMessages = async (channelId: string) => {
    const r = await api.get<Paged<MessageDto>>(`/api/channels/${channelId}/messages`);
    setMessages(r.items);
  };

  useEffect(() => { loadChannels(); loadMembers(); }, []);
  useEffect(() => {
    if (active) { loadMessages(active.id); api.post(`/api/channels/${active.id}/read`, {}).catch(() => {}); }
  }, [active?.id]);
  useEffect(() => { endRef.current?.scrollIntoView({ behavior: 'smooth' }); }, [messages]);

  const openDirect = async (userId: string) => {
    const c = await api.post<ChannelDto>('/api/channels/direct', { userId });
    setChannels((prev) => prev.some((x) => x.id === c.id) ? prev : [...prev, c]);
    setActive(c);
  };

  const send = async (e: FormEvent) => {
    e.preventDefault();
    if (!active || !body.trim()) return;
    await api.post(`/api/channels/${active.id}/messages`, { body });
    setBody('');
    await loadMessages(active.id);
  };

  const projectChannels = channels.filter((c) => c.type === 0);

  return (
    <div className="chat-layout">
      <aside className="card chat-side">
        <h3 style={{ padding: '4px 6px' }}>Mensajes directos</h3>
        {members.map((m) => (
          <button key={m.userId} className="nav-item" style={{ width: '100%', textAlign: 'left', background: 'transparent', border: 'none' }} onClick={() => openDirect(m.userId)}>
            <span className="avatar">{initials(m.name)}</span> <span>{m.name}</span>
          </button>
        ))}
        {members.length === 0 && <div className="muted small" style={{ padding: 8 }}>No hay otros miembros.</div>}

        <h3 style={{ padding: '10px 6px 4px' }}>Canales</h3>
        {projectChannels.map((c) => (
          <button key={c.id} className={`nav-item ${active?.id === c.id ? 'active' : ''}`} style={{ width: '100%', textAlign: 'left', background: 'transparent', border: 'none' }} onClick={() => setActive(c)}>
            # {c.name} {c.projectCode && <span className="muted small">({c.projectCode})</span>}
          </button>
        ))}
        {projectChannels.length === 0 && <div className="muted small" style={{ padding: 8 }}>Sin canales.</div>}
      </aside>

      <section className="card chat-main">
        <div className="chat-head row">
          {active?.type === 1 && <span className="avatar">{initials(active.name)}</span>}
          <span className="grow">{active ? (active.type === 1 ? active.name : `# ${active.name}`) : 'Selecciona una conversación'}</span>
        </div>
        <div className="chat-body">
          {messages.map((m) => {
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
          <input className="input grow" placeholder="Escribe un mensaje…" value={body} onChange={(e) => setBody(e.target.value)} />
          <button className="btn primary" disabled={!active}>Enviar</button>
        </form>
      </section>
    </div>
  );
}
