import { createContext, useCallback, useContext, useEffect, useRef, type ReactNode } from 'react';
import { useBlocker, type BlockerFunction } from 'react-router-dom';

interface PendingChangesGuard {
  hasPendingChanges: () => boolean;
  saveBeforeLeaving: () => Promise<boolean>;
}

interface NavigationGuardContextValue {
  register: (guard: PendingChangesGuard) => () => void;
  saveBeforeLeaving: () => Promise<boolean>;
  lockNavigation: () => () => void;
}

const NavigationGuardContext = createContext<NavigationGuardContextValue | null>(null);

/** Keep the current screen mounted until its pending writes have succeeded. */
export function NavigationGuardProvider({ children }: { children: ReactNode }) {
  const guards = useRef(new Set<PendingChangesGuard>());
  const pendingSave = useRef<Promise<boolean> | null>(null);
  const navigationLocks = useRef(new Set<symbol>());
  const blockedByLock = useRef(false);

  const lockNavigation = useCallback(() => {
    const token = Symbol('navigation-lock');
    navigationLocks.current.add(token);
    return () => { navigationLocks.current.delete(token); };
  }, []);

  const register = useCallback((guard: PendingChangesGuard) => {
    guards.current.add(guard);
    return () => { guards.current.delete(guard); };
  }, []);

  const saveBeforeLeaving = useCallback((): Promise<boolean> => {
    if (pendingSave.current) return pendingSave.current;
    const save = async () => {
      try {
        for (const guard of guards.current) {
          if (guard.hasPendingChanges() && !(await guard.saveBeforeLeaving())) return false;
        }
        return true;
      } catch {
        return false;
      }
    };
    const attempt = save();
    pendingSave.current = attempt;
    void attempt.finally(() => { if (pendingSave.current === attempt) pendingSave.current = null; });
    return attempt;
  }, []);

  const blocker = useBlocker(useCallback<BlockerFunction>(({ currentLocation, nextLocation }) => {
    blockedByLock.current = navigationLocks.current.size > 0;
    return blockedByLock.current ||
      ((currentLocation.pathname !== nextLocation.pathname || currentLocation.search !== nextLocation.search || currentLocation.hash !== nextLocation.hash)
      && [...guards.current].some((guard) => guard.hasPendingChanges()));
  }, []));

  useEffect(() => {
    if (blocker.state !== 'blocked') return;
    // A destination attempted during logout must never be replayed afterwards.
    if (blockedByLock.current) { blocker.reset(); return; }
    let current = true;
    void saveBeforeLeaving().then((saved) => {
      if (current) { if (saved && !navigationLocks.current.size) blocker.proceed(); else blocker.reset(); }
    });
    return () => { current = false; };
  }, [blocker, saveBeforeLeaving]);

  useEffect(() => {
    const warn = (event: BeforeUnloadEvent) => {
      if ([...guards.current].some((guard) => guard.hasPendingChanges())) {
        event.preventDefault();
        event.returnValue = '';
      }
    };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, []);

  return <NavigationGuardContext.Provider value={{ register, saveBeforeLeaving, lockNavigation }}>{children}</NavigationGuardContext.Provider>;
}

export function useNavigationGuard() {
  const value = useContext(NavigationGuardContext);
  if (!value) throw new Error('NavigationGuardProvider no está disponible.');
  return value;
}
