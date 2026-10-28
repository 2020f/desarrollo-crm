import { useEffect, useState, FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api';
import type { ProjectDetailDto, ReportSnapshotDto, MemberDto, RoleDto } from '../types';
import { PROJECT_STATUS_LABELS, PRIORITY_LABELS, fmtDate } from '../constants';

export default function ProjectDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [p, setP] = useState<ProjectDetailDto | null>(null);
  const [report, setReport] = useState<ReportSnapshotDto | null>(null);
  const [orgMembers, setOrgMembers] = useState<MemberDto[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [showAdd, setShowAdd] = useState(false);
  const [error, setError] = useState('');

  const load = async () => {
    if (!id) return;
    try {
      const [detail, rep, om, rl] = await Promise.all([
        api.get<ProjectDetailDto>(`/api/projects/${id}`),
        api.get<ReportSnapshotDto>(`/api/reports/project/${id}`),
        api.get<MemberDto[]>('/api/organizations/members'),
        api.get<RoleDto[]>('/api/organizations/roles'),
      ]);
      setP(detail);
      setReport(rep);
      setOrgMembers(om);
      setRoles(rl);
    } catch (e) { setError((e as Error).message); }
  };

  useEffect(() => { load(); }, [id]);

  const addMember = async (userId: string, roleId: string) => {
    await api.post(`/api/projects/${id}/members`, { userId, roleId: roleId || null });
    setShowAdd(false);
    load();
  };
  const removeMember = async (userId: string) => {
    if (!confirm('¿Quitar a este miembro del proyecto?')) return;
    await api.del(`/api/projects/${id}/members/${userId}`);
    load();
  };
  const changeRole = async (userId: string, roleId: string) => {
    await api.put(`/api/projects/${id}/members/${userId}/role`, { roleId: roleId || null });
    load();
  };

  if (error) return <div className="alert error">{error}</div>;
  if (!p) return <div className="empty">Cargando…</div>;

  const s = p.summary;
  const done = report ? Math.round((report.doneTasks / Math.max(1, report.totalTasks)) * 100) : 0;

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <div>
          <div className="muted small">{s.code} · {PROJECT_STATUS_LABELS[s.status]}</div>
          <h1 className="page-title" style={{ margin: 0 }}>{s.name}</h1>
        </div>
        <div className="grow" />
        <Link className="btn" to={`/projects/${id}/backlog`}>Backlog</Link>
        <Link className="btn" to={`/projects/${id}/table`}>Lista</Link>
        <Link className="btn" to={`/projects/${id}/board`}>Tablero</Link>
        <Link className="btn primary" to={`/projects/${id}/diagram`}>Diagrama</Link>
      </div>

      {s.objective && <div className="card" style={{ marginBottom: 16 }}><h3>Objetivo</h3><p className="muted">{s.objective}</p></div>}

      <div className="two-col">
        <section className="card">
          <h3>Avance</h3>
          <div className="progress"><div className="progress-fill" style={{ width: `${done}%` }} /></div>
          <div className="small muted mt-8">{report?.doneTasks} de {report?.totalTasks} tareas finalizadas · {report?.blockedTasks} bloqueadas · {report?.overdueTasks} vencidas</div>
        </section>
        <section className="card">
          <h3>Detalles</h3>
          <dl className="kv">
            <dt>Prioridad</dt><dd>{PRIORITY_LABELS[s.priority]}</dd>
            <dt>Entrega</dt><dd>{fmtDate(s.dueDate) || '—'}</dd>
            <dt>Privado</dt><dd>{s.isPrivate ? 'Sí' : 'No'}</dd>
          </dl>
        </section>

        <section className="card">
          <div className="row" style={{ marginBottom: 8 }}>
            <h3 style={{ margin: 0 }}>Equipo ({p.members.length})</h3>
            <div className="grow" />
            <button className="btn sm primary" onClick={() => setShowAdd(true)}>+ Añadir</button>
          </div>
          {p.members.map((m) => (
            <div key={m.id} className="row" style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}>
              <span className="avatar">{m.name[0]}</span>
              <div className="grow" style={{ minWidth: 0 }}>
                <div style={{ fontWeight: 600 }}>{m.name}</div>
                <div className="muted small">{m.email}</div>
              </div>
              <select className="input sm" value={m.roleId || ''} onChange={(e) => changeRole(m.userId, e.target.value)}>
                <option value="">Sin rol</option>
                {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
              </select>
              <button className="btn ghost sm" onClick={() => removeMember(m.userId)} title="Quitar">✕</button>
            </div>
          ))}
          {p.members.length === 0 && <div className="muted small">Sin miembros. Añade al equipo para poder asignar tareas.</div>}
        </section>

        <section className="card">
          <h3>Hitos</h3>
          {p.milestones.map((m) => (
            <div key={m.id} className="row" style={{ padding: '4px 0' }}><span>◆</span><span className="grow">{m.title}</span><span className="muted small">{fmtDate(m.dueDate)}</span></div>
          ))}
          {p.milestones.length === 0 && <div className="muted small">Sin hitos.</div>}
        </section>
      </div>

      {p.risks && <div className="card mt-16"><h3>Riesgos</h3><p className="muted">{p.risks}</p></div>}

      {showAdd && (
        <AddMemberModal orgMembers={orgMembers} roles={roles} current={p.members.map((m) => m.userId)}
          onClose={() => setShowAdd(false)} onAdd={addMember} />
      )}
    </div>
  );
}

function AddMemberModal({ orgMembers, roles, current, onClose, onAdd }: {
  orgMembers: MemberDto[]; roles: RoleDto[]; current: string[]; onClose: () => void; onAdd: (userId: string, roleId: string) => void;
}) {
  const [userId, setUserId] = useState('');
  const [roleId, setRoleId] = useState('');
  const available = orgMembers.filter((m) => !current.includes(m.userId));

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal card" onClick={(e) => e.stopPropagation()}>
        <h3>Añadir miembro al equipo</h3>
        <div className="field">
          <label>Miembro de la organización</label>
          <select className="input" value={userId} onChange={(e) => setUserId(e.target.value)}>
            <option value="">Selecciona…</option>
            {available.map((m) => <option key={m.userId} value={m.userId}>{m.name} ({m.email})</option>)}
          </select>
        </div>
        <div className="field">
          <label>Rol en el proyecto</label>
          <select className="input" value={roleId} onChange={(e) => setRoleId(e.target.value)}>
            <option value="">Sin rol</option>
            {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
          </select>
        </div>
        {available.length === 0 && <div className="muted small">No hay más miembros de la organización disponibles. Créalos en Administración.</div>}
        <div className="row" style={{ justifyContent: 'flex-end' }}>
          <button className="btn" onClick={onClose}>Cancelar</button>
          <button className="btn primary" disabled={!userId} onClick={() => onAdd(userId, roleId)}>Añadir</button>
        </div>
      </div>
    </div>
  );
}
