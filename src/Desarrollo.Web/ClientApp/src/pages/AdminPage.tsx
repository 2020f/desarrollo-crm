import { useEffect, useState, FormEvent } from 'react';
import { api } from '../api';
import type { MemberDto, RoleDto, ProductDto } from '../types';

export default function AdminPage() {
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [showCreate, setShowCreate] = useState(false);

  const load = () => {
    api.get<MemberDto[]>('/api/organizations/members').then(setMembers).catch(() => {});
    api.get<RoleDto[]>('/api/organizations/roles').then(setRoles).catch(() => {});
    api.get<ProductDto[]>('/api/organizations/products').then(setProducts).catch(() => {});
  };

  useEffect(() => { load(); }, []);

  const changeRole = async (userId: string, roleId: string) => {
    await api.put(`/api/admin/members/${userId}/role`, { roleId: roleId || null });
    load();
  };

  return (
    <div>
      <div className="row" style={{ marginBottom: 16 }}>
        <h1 className="page-title" style={{ margin: 0 }}>Administración</h1>
        <div className="grow" />
        <button className="btn primary" onClick={() => setShowCreate(true)}>+ Crear usuario</button>
      </div>

      <div className="two-col">
        <section className="card">
          <h3>Miembros ({members.length})</h3>
          {members.map((m) => (
            <div key={m.id} className="row" style={{ padding: '8px 0', borderBottom: '1px solid var(--border)' }}>
              <span className="avatar">{m.name[0]}</span>
              <div className="grow" style={{ minWidth: 0 }}>
                <div style={{ fontWeight: 600 }}>{m.name}</div>
                <div className="muted small">{m.email}</div>
              </div>
              <select className="input sm" value={m.roleId || ''} onChange={(e) => changeRole(m.userId, e.target.value)}>
                {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
              </select>
            </div>
          ))}
          {members.length === 0 && <div className="muted small">Sin miembros.</div>}
        </section>

        <section className="card">
          <h3>Roles</h3>
          {roles.map((r) => (
            <div key={r.id} style={{ padding: '6px 0', borderBottom: '1px solid var(--border)' }}>
              <div className="row"><span style={{ fontWeight: 600 }}>{r.name}</span>{r.isSystem && <span className="badge">Sistema</span>}</div>
              <div className="muted small">{r.description}</div>
            </div>
          ))}
        </section>

        <section className="card">
          <h3>Productos</h3>
          {products.map((p) => (
            <div key={p.id} className="row" style={{ padding: '6px 0' }}>
              <span className="grow">{p.name}</span>
              <span className={`badge ${p.isActive ? 'success' : 'error'}`}>{p.isActive ? 'Activo' : 'Inactivo'}</span>
            </div>
          ))}
          {products.length === 0 && <div className="muted small">Sin productos.</div>}
        </section>
      </div>

      {showCreate && <CreateUserModal roles={roles} onClose={() => setShowCreate(false)} onCreated={() => { setShowCreate(false); load(); }} />}
    </div>
  );
}

function CreateUserModal({ roles, onClose, onCreated }: { roles: RoleDto[]; onClose: () => void; onCreated: () => void }) {
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [roleId, setRoleId] = useState('');
  const [error, setError] = useState('');
  const [created, setCreated] = useState<{ email: string; fullName: string } | null>(null);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    try {
      const r = await api.post<{ email: string; fullName: string }>('/api/admin/users', { fullName, email, password, roleId: roleId || null });
      setCreated(r);
    } catch (err) { setError((err as Error).message); }
  };

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal card" onClick={(e) => e.stopPropagation()}>
        <h3>Crear usuario</h3>
        {created ? (
          <div>
            <div className="alert success">Usuario creado. Comparte estas credenciales con {created.fullName}:</div>
            <div className="card" style={{ background: 'var(--nav)' }}>
              <div><strong>Correo:</strong> {created.email}</div>
              <div><strong>Contraseña:</strong> {password}</div>
            </div>
            <button className="btn primary mt-16" style={{ width: '100%' }} onClick={onCreated}>Listo</button>
          </div>
        ) : (
          <form onSubmit={submit}>
            {error && <div className="alert error">{error}</div>}
            <div className="field"><label>Nombre</label><input className="input" value={fullName} onChange={(e) => setFullName(e.target.value)} required /></div>
            <div className="field"><label>Correo</label><input className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required /></div>
            <div className="field"><label>Contraseña</label><input className="input" type="password" autoComplete="new-password" value={password} onChange={(e) => setPassword(e.target.value)} required /></div>
            <div className="field">
              <label>Rol</label>
              <select className="input" value={roleId} onChange={(e) => setRoleId(e.target.value)}>
                <option value="">Sin rol de organización</option>
                {roles.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
              </select>
            </div>
            <div className="row" style={{ justifyContent: 'flex-end' }}>
              <button type="button" className="btn" onClick={onClose}>Cancelar</button>
              <button className="btn primary">Crear</button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
