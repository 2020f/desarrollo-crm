import { FormEvent, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../App';

export default function RegisterPage() {
  const { refresh } = useAuth();
  const navigate = useNavigate();
  const [fullName, setFullName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [organizationName, setOrganizationName] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [policy, setPolicy] = useState<'loading' | 'open' | 'closed' | 'error'>('loading');
  useEffect(() => {
    let active = true;
    api.get<{ enabled: boolean }>('/api/account/registration').then((value) => { if (active) setPolicy(value.enabled ? 'open' : 'closed'); })
      .catch(() => { if (active) setPolicy('error'); });
    return () => { active = false; };
  }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (policy !== 'open') return;
    setError('');
    if (password.length < 8) { setError('La contraseña debe tener al menos 8 caracteres e incluir un número.'); return; }
    setBusy(true);
    try {
      await api.post('/api/account/register', { fullName, email, password, organizationName });
      await refresh();
      navigate('/');
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="auth-screen">
      <div className="auth-card card">
        <div className="auth-brand"><span className="brand-dot" /> Desarrollo</div>
        <h2>Crear equipo</h2>
        <p className="muted small">{policy === 'open' ? 'Registra tu cuenta y tu organización. Serás el administrador.' : policy === 'closed' ? 'El registro público está cerrado. Solicita una cuenta al administrador de tu equipo.' : policy === 'error' ? 'No se pudo comprobar si el registro está disponible. Recarga para reintentar.' : 'Comprobando disponibilidad del registro…'}</p>
        {error && <div className="alert error">{error}</div>}
        {policy === 'open' && <form onSubmit={submit}>
          <div className="field"><label>Nombre</label><input className="input" value={fullName} onChange={(e) => setFullName(e.target.value)} required /></div>
          <div className="field"><label>Correo</label><input className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required /></div>
          <div className="field"><label>Contraseña</label><input className="input" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required /></div>
          <div className="field"><label>Nombre de la organización</label><input className="input" value={organizationName} onChange={(e) => setOrganizationName(e.target.value)} placeholder="Equipo de desarrollo" required /></div>
          <button className="btn primary" style={{ width: '100%' }} disabled={busy}>{busy ? 'Creando…' : 'Crear cuenta'}</button>
        </form>}
        <div className="mt-16 small muted">¿Ya tienes cuenta? <Link to="/login">Iniciar sesión</Link></div>
      </div>
    </div>
  );
}
