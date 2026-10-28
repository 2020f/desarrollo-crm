import { FormEvent, useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../App';

export default function LoginPage() {
  const { refresh } = useAuth();
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [registrationOpen, setRegistrationOpen] = useState(false);
  useEffect(() => {
    let active = true;
    api.get<{ enabled: boolean }>('/api/account/registration').then((value) => { if (active) setRegistrationOpen(value.enabled); }).catch(() => {});
    return () => { active = false; };
  }, []);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setBusy(true);
    try {
      await api.post('/api/account/login', { username, password, rememberMe: true });
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
        <h2>Iniciar sesión</h2>
        <p className="muted small">Accede a tu espacio de trabajo.</p>
        {error && <div className="alert error">{error}</div>}
        <form onSubmit={submit}>
          <div className="field">
            <label>Correo o usuario</label>
            <input className="input" value={username} onChange={(e) => setUsername(e.target.value)} autoFocus required />
          </div>
          <div className="field">
            <label>Contraseña</label>
            <input className="input" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>
          <button className="btn primary" style={{ width: '100%' }} disabled={busy}>
            {busy ? 'Entrando…' : 'Entrar'}
          </button>
        </form>
        <div className="mt-16 small muted">
          {registrationOpen ? <>¿No tienes cuenta? <Link to="/register">Crear equipo</Link></> : 'Solicita tu cuenta al administrador de tu equipo.'}
        </div>
      </div>
    </div>
  );
}
