import { useEffect, useRef, useState } from 'react';
import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../App';
import { api } from '../api';
import { initials } from '../constants';
import { useNavigationGuard } from './NavigationGuard';
import { useLiveActivity } from '../live/LiveActivityProvider';

const NAV = [
  { to: '/', label: 'Inicio', icon: '⌂', end: true },
  { to: '/my-work', label: 'Mi trabajo', icon: '▣' },
  { to: '/projects', label: 'Proyectos', icon: '▤' },
  { to: '/tickets', label: 'Solicitudes', icon: '✉' },
  { to: '/calendar', label: 'Calendario', icon: '▦' },
  { to: '/docs', label: 'Documentos', icon: '▤' },
  { to: '/folders', label: 'Carpetas', icon: '▱' },
  { to: '/chat', label: 'Chat', icon: '✎' },
  { to: '/reports', label: 'Reportes', icon: '◔' },
  { to: '/admin', label: 'Administración', icon: '⚙' },
];

export default function Layout() {
  const { user, refresh } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const { saveBeforeLeaving, lockNavigation } = useNavigationGuard();
  const { unreadCount: unread, soundEnabled, soundReady, soundError, toggleSound, connected, accessLost } = useLiveActivity();
  const [loggingOut, setLoggingOut] = useState(false);
  const [logoutError, setLogoutError] = useState('');
  const loggingOutRef = useRef(false);
  const shellRef = useRef<HTMLDivElement>(null);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const menuToggleRef = useRef<HTMLButtonElement>(null);
  const menuCloseRef = useRef<HTMLButtonElement>(null);
  const sidebarRef = useRef<HTMLElement>(null);

  useEffect(() => { setMobileMenuOpen(false); }, [location.key]);

  useEffect(() => {
    if (!mobileMenuOpen) return;
    menuCloseRef.current?.focus();
    const onKey = (event: KeyboardEvent) => {
      if (loggingOutRef.current) return;
      if (event.key === 'Escape') { event.preventDefault(); setMobileMenuOpen(false); return; }
      if (event.key !== 'Tab') return;
      const controls = Array.from(sidebarRef.current?.querySelectorAll<HTMLElement>('a[href], button:not(:disabled), input:not(:disabled), select:not(:disabled), [tabindex="0"]') || []);
      const first = controls[0], last = controls[controls.length - 1];
      if (!sidebarRef.current?.contains(document.activeElement)) { event.preventDefault(); first?.focus(); }
      else if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    };
    const onResize = () => { if (window.innerWidth > 700) setMobileMenuOpen(false); };
    document.addEventListener('keydown', onKey);
    window.addEventListener('resize', onResize);
    return () => {
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', onResize);
      if (menuToggleRef.current?.isConnected) menuToggleRef.current.focus();
    };
  }, [mobileMenuOpen]);

  const logout = async () => {
    if (loggingOutRef.current) return;
    loggingOutRef.current = true; setLoggingOut(true); setLogoutError('');
    const unlockNavigation = lockNavigation();
    // Lock immediately, including the interval after saves and before logout
    // and session refresh finish. inert covers pointer, keyboard and form input.
    shellRef.current?.setAttribute('inert', '');
    let completed = false;
    try {
      // Preserve the authenticated session if a pending diagram save fails.
      if (!(await saveBeforeLeaving())) return;
      await api.post('/api/account/logout', {});
      await refresh();
      completed = true;
    } catch (error) { setLogoutError('No se pudo cerrar la sesión: ' + (error as Error).message); }
    finally {
      unlockNavigation();
      shellRef.current?.removeAttribute('inert');
      loggingOutRef.current = false; setLoggingOut(false);
    }
    if (completed) navigate('/login');
  };

  return (
    <><div ref={shellRef} aria-busy={loggingOut} style={{ display: 'flex', height: '100vh', overflow: 'hidden' }}>
      {mobileMenuOpen && <button className="sidebar-backdrop" aria-label="Cerrar navegación" tabIndex={-1} onClick={() => setMobileMenuOpen(false)} />}
      <aside id="app-navigation" className={'sidebar' + (mobileMenuOpen ? ' mobile-open' : '')} ref={sidebarRef} role={mobileMenuOpen ? 'dialog' : undefined} aria-modal={mobileMenuOpen || undefined} aria-label="Navegación principal">
        <button ref={menuCloseRef} className="btn mobile-sidebar-close" onClick={() => setMobileMenuOpen(false)} aria-label="Cerrar navegación">✕</button>
        <div className="brand">
          <span className="brand-dot" /> Desarrollo
        </div>
        <div className="org-switcher">
          <span className="avatar">{initials(user?.organizationName || 'D')}</span>
          <span className="org-name">{user?.organizationName || 'Equipo de desarrollo'}</span>
        </div>
        <nav className="nav">
          {NAV.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end}
              className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
              <span className="nav-icon">{item.icon}</span>
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-footer">
          <div className="row">
            <span className="avatar">{initials(user?.name)}</span>
            <div className="grow" style={{ minWidth: 0 }}>
              <div style={{ fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{user?.name}</div>
              <div className="muted small">{user?.email}</div>
            </div>
            <button className="btn ghost sm" onClick={() => void logout()} disabled={loggingOut} title="Cerrar sesión" aria-label="Cerrar sesión">⏻</button>
          </div>
        </div>
      </aside>

      <div className="main">
        <header className="topbar">
          <button ref={menuToggleRef} className="btn mobile-menu-toggle" onClick={() => setMobileMenuOpen(!mobileMenuOpen)} aria-label="Abrir navegación" aria-controls="app-navigation" aria-expanded={mobileMenuOpen}>☰</button>
          <div className="topbar-search">
            <input className="input" placeholder="Buscar tareas, proyectos, tickets…" style={{ width: '100%' }} />
          </div>
          <button className="btn sm" onClick={() => navigate('/projects')}>+ Crear</button>
          <button className="btn sm live-sound" data-sound-toggle onClick={() => void toggleSound()} aria-pressed={soundEnabled && soundReady}
            title={soundEnabled && soundReady ? 'Silenciar avisos de mensajes y notificaciones' : 'Activar avisos de mensajes y notificaciones'}>
            {soundEnabled && soundReady ? 'Sonido activado' : 'Activar sonido'}
          </button>
          <button className="icon-btn" onClick={() => navigate('/notifications')} title="Notificaciones">
            🔔 {unread > 0 && <span className="dot-count">{unread}</span>}
          </button>
        </header>
        <main className="content">
          {!connected && <div className="live-connection muted small" role="status">{accessLost ? 'Tu acceso cambió. Comprueba tu sesión antes de continuar.' : 'Conectando las actualizaciones automáticas. Si no hay conexión, se reintentará.'}</div>}
          {soundError && <div className="alert error" role="alert">{soundError}</div>}
          {logoutError && <div className="alert error" role="alert">{logoutError}</div>}
          <Outlet />
        </main>
      </div>
    </div>
    {loggingOut && <div role="status" aria-live="polite" style={{ position: 'fixed', inset: 0, zIndex: 2000, display: 'grid', placeItems: 'center', background: '#0B102055', cursor: 'wait' }}><div className="card">Guardando cambios y cerrando sesión…</div></div>}
    </>
  );
}
