import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { api } from '../api';
import { createAlertSound } from './sound';

export interface ActivitySnapshot {
  observedAt: string;
  channels: { id: string; latestMessageId?: string | null; latestMessageAuthorId?: string | null; latestMessageAt?: string | null; latestIncomingMessageId?: string | null; latestIncomingMessageAt?: string | null; changedAt?: string | null; unreadCount: number }[];
  notifications: { unreadCount: number; items: { id: string; createdAt: string }[] };
}

const emptyState = { snapshot: null as ActivitySnapshot | null, chatVersion: 0, notificationVersion: 0, connected: false, accessLost: false };
const LiveContext = createContext({ ...emptyState, unreadCount: 0, soundEnabled: false, soundReady: false, soundError: '', toggleSound: async () => {}, refresh: async () => {} });
export const useLiveActivity = () => useContext(LiveContext);

/** One monitor per signed-in shell; page navigation keeps its baseline and sound deduplication. */
export function LiveActivityProvider({ userId, organizationId, children }: { userId: string | null; organizationId: string | null; children: ReactNode }) {
  const [state, setState] = useState(emptyState);
  const [soundEnabled, setSoundEnabled] = useState(false);
  const [soundReady, setSoundReady] = useState(false);
  const [soundError, setSoundError] = useState('');
  const sound = useRef<ReturnType<typeof createAlertSound> | null>(null);
  const enabledRef = useRef(false), readyRef = useRef(false);
  const refreshRef = useRef<() => Promise<void>>(async () => {});
  const unlockRef = useRef<Promise<boolean> | null>(null);
  const preferenceKey = `crm:sound:${organizationId}:${userId}`;

  const unlock = useCallback(async () => {
    const engine = sound.current;
    if (!engine) return false;
    if (!unlockRef.current) unlockRef.current = engine.unlock();
    const ready = await unlockRef.current;
    unlockRef.current = null;
    if (sound.current !== engine) return false;
    readyRef.current = ready; setSoundReady(ready);
    setSoundError(ready ? '' : 'No se pudo activar el audio. Comprueba el sonido del navegador y vuelve a intentarlo.');
    return ready;
  }, []);

  useEffect(() => {
    const engine = createAlertSound(); sound.current = engine;
    let enabled = true;
    try { enabled = window.localStorage.getItem(preferenceKey) !== 'off'; } catch { /* private browsing may restrict storage */ }
    enabledRef.current = enabled; readyRef.current = false;
    setSoundEnabled(enabled); setSoundReady(false); setSoundError('');
    const activate = (event: Event) => {
      if ((event.target as Element | null)?.closest?.('[data-sound-toggle]')) return;
      if (enabledRef.current) void unlock();
    };
    window.addEventListener('pointerdown', activate);
    window.addEventListener('keydown', activate);
    return () => {
      window.removeEventListener('pointerdown', activate); window.removeEventListener('keydown', activate);
      sound.current = null; unlockRef.current = null; readyRef.current = false; engine.close();
    };
  }, [preferenceKey, unlock]);

  const toggleSound = useCallback(async () => {
    const enabled = !(enabledRef.current && readyRef.current);
    enabledRef.current = enabled; setSoundEnabled(enabled); setSoundError('');
    try { window.localStorage.setItem(preferenceKey, enabled ? 'on' : 'off'); } catch { /* keep this session's preference */ }
    if (enabled && await unlock()) sound.current?.play('notification');
  }, [preferenceKey, unlock]);

  useEffect(() => {
    setState(emptyState);
    if (!userId || !organizationId) return;
    let disposed = false, queued = false;
    let request: AbortController | null = null;
    let timer: number | undefined;
    let baseline: number | null = null;
    const seenMessages = new Set<string>(), seenNotifications = new Set<string>();
    let chatSignature = '', notificationSignature = '';

    const poll = async () => {
      if (disposed) return;
      if (request) { queued = true; return; }
      window.clearTimeout(timer);
      const controller = new AbortController(); request = controller;
      const timeout = window.setTimeout(() => controller.abort(), 10000);
      try {
        const snapshot = await api.get<ActivitySnapshot>('/api/activity', controller.signal);
        if (disposed) return;
        let newMessage = false, newNotification = false;
        const initial = baseline === null;
        for (const channel of snapshot.channels) {
          const incomingId = channel.latestIncomingMessageId ?? (channel.latestMessageAuthorId !== userId ? channel.latestMessageId : null);
          const incomingAt = channel.latestIncomingMessageAt ?? channel.latestMessageAt;
          if (!incomingId) continue;
          if (!initial && !seenMessages.has(incomingId) && Date.parse(incomingAt || '') >= baseline!) newMessage = true;
          seenMessages.add(incomingId);
        }
        for (const item of snapshot.notifications.items) {
          if (!initial && !seenNotifications.has(item.id) && Date.parse(item.createdAt) >= baseline!) newNotification = true;
          seenNotifications.add(item.id);
        }
        if (initial) baseline = Date.parse(snapshot.observedAt);
        if (enabledRef.current && readyRef.current && (newMessage || newNotification)) sound.current?.play(newMessage ? 'message' : 'notification');
        const nextChat = JSON.stringify(snapshot.channels.map((c) => [c.id, c.latestMessageId, c.changedAt]).sort((a, b) => String(a[0]).localeCompare(String(b[0]))));
        const nextNotifications = JSON.stringify([snapshot.notifications.unreadCount, snapshot.notifications.items.map((n) => n.id).sort()]);
        const chatChanged = nextChat !== chatSignature, notificationsChanged = nextNotifications !== notificationSignature;
        chatSignature = nextChat; notificationSignature = nextNotifications;
        setState((previous) => ({ snapshot, connected: true, accessLost: false,
          chatVersion: previous.chatVersion + (chatChanged ? 1 : 0), notificationVersion: previous.notificationVersion + (notificationsChanged ? 1 : 0) }));
      } catch (failure) {
        if (disposed) return;
        const status = (failure as Error & { status?: number }).status;
        const accessLost = status === 401 || status === 403;
        setState((previous) => ({ ...previous, connected: false, accessLost: accessLost || previous.accessLost,
          snapshot: accessLost || previous.accessLost ? null : previous.snapshot,
          chatVersion: previous.chatVersion + (accessLost && !previous.accessLost ? 1 : 0),
          notificationVersion: previous.notificationVersion + (accessLost && !previous.accessLost ? 1 : 0) }));
      } finally {
        window.clearTimeout(timeout); request = null;
        if (!disposed) { timer = window.setTimeout(() => { void poll(); }, queued ? 0 : 3000); queued = false; }
      }
    };
    refreshRef.current = poll;
    const wake = () => { void poll(); };
    const visible = () => { if (document.visibilityState === 'visible') wake(); };
    window.addEventListener('focus', wake); window.addEventListener('online', wake); document.addEventListener('visibilitychange', visible);
    void poll();
    return () => {
      disposed = true; window.clearTimeout(timer); request?.abort(); refreshRef.current = async () => {};
      window.removeEventListener('focus', wake); window.removeEventListener('online', wake); document.removeEventListener('visibilitychange', visible);
    };
  }, [userId, organizationId]);

  const refresh = useCallback(async () => { await refreshRef.current(); }, []);
  return <LiveContext.Provider value={{ ...state, unreadCount: state.snapshot?.notifications.unreadCount ?? 0,
    soundEnabled, soundReady, soundError, toggleSound, refresh }}>{children}</LiveContext.Provider>;
}
