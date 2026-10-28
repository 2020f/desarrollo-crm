import { createContext, lazy, Suspense, useContext, useEffect, useState, ReactNode } from 'react';
import { Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { api, refreshCsrf, setUnauthorizedHandler } from './api';
import type { CurrentUser } from './types';
import Layout from './components/Layout';
import { LiveActivityProvider } from './live/LiveActivityProvider';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import HomePage from './pages/HomePage';
import MyWorkPage from './pages/MyWorkPage';
import ProjectsPage from './pages/ProjectsPage';
import ProjectDetailPage from './pages/ProjectDetailPage';
import BoardPage from './pages/BoardPage';
import TablePage from './pages/TablePage';
import BacklogPage from './pages/BacklogPage';
import TaskDetailPage from './pages/TaskDetailPage';
import TaskDiagramPage from './pages/TaskDiagramPage';
import CalendarPage from './pages/CalendarPage';
import TicketsPage from './pages/TicketsPage';
import TicketDetailPage from './pages/TicketDetailPage';
import NotificationsPage from './pages/NotificationsPage';
import ChatPage from './pages/ChatPage';
import FilesPage from './pages/FilesPage';
import ReleasesPage from './pages/ReleasesPage';
import ReviewPage from './pages/ReviewPage';
import ReportsPage from './pages/ReportsPage';
import AdminPage from './pages/AdminPage';

const DocumentsPage = lazy(() => import('./pages/DocumentsPage'));

interface AuthState {
  user: CurrentUser | null;
  loading: boolean;
  refresh: () => Promise<void>;
}

const AuthContext = createContext<AuthState>({ user: null, loading: true, refresh: async () => {} });

export function useAuth() {
  return useContext(AuthContext);
}

function Protected({ children }: { children: ReactNode }) {
  const { user, loading } = useAuth();
  const location = useLocation();
  if (loading) return <div className="empty">Cargando…</div>;
  if (!user?.isAuthenticated) return <Navigate to="/login" state={{ from: location }} replace />;
  return <LiveActivityProvider userId={user.id} organizationId={user.organizationId}>{children}</LiveActivityProvider>;
}

export default function App() {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [loading, setLoading] = useState(true);

  const refresh = async () => {
    try {
      await refreshCsrf();
      const res = await fetch('/api/account/current', { credentials: 'same-origin' });
      if (res.ok) {
        setUser(await res.json());
      } else {
        setUser(null);
      }
    } catch {
      setUser(null);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    setUnauthorizedHandler(() => { refresh(); });
    refresh();
  }, []);

  return (
    <AuthContext.Provider value={{ user, loading, refresh }}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/" element={<Protected><Layout /></Protected>}>
          <Route index element={<HomePage />} />
          <Route path="my-work" element={<MyWorkPage />} />
          <Route path="projects" element={<ProjectsPage />} />
          <Route path="projects/:id" element={<ProjectDetailPage />} />
          <Route path="projects/:id/board" element={<BoardPage />} />
          <Route path="projects/:id/table" element={<TablePage />} />
          <Route path="projects/:id/backlog" element={<BacklogPage />} />
          <Route path="projects/:id/diagram" element={<TaskDiagramPage />} />
          <Route path="tasks/:id" element={<TaskDetailPage />} />
          <Route path="calendar" element={<CalendarPage />} />
          <Route path="tickets" element={<TicketsPage />} />
          <Route path="tickets/:id" element={<TicketDetailPage />} />
          <Route path="notifications" element={<NotificationsPage />} />
          <Route path="chat" element={<ChatPage />} />
          <Route path="docs" element={<Suspense fallback={<div className="empty">Cargando documentos…</div>}><DocumentsPage /></Suspense>} />
          <Route path="folders" element={<FilesPage />} />
          <Route path="files" element={<FilesPage />} />
          <Route path="releases" element={<ReleasesPage />} />
          <Route path="review" element={<ReviewPage />} />
          <Route path="reports" element={<ReportsPage />} />
          <Route path="admin" element={<AdminPage />} />
          <Route path="*" element={<div className="empty">Página no encontrada</div>} />
        </Route>
      </Routes>
    </AuthContext.Provider>
  );
}
