let csrfToken: string | null = null;
let onUnauthorized: (() => void) | null = null;

export function setUnauthorizedHandler(fn: () => void) {
  onUnauthorized = fn;
}

export async function refreshCsrf(): Promise<void> {
  try {
    const res = await fetch('/api/account/antiforgery', { credentials: 'same-origin' });
    if (res.ok) {
      const data = await res.json();
      csrfToken = data.token;
    }
  } catch {
    /* ignore */
  }
}

async function request<T>(url: string, init: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = { ...(init.headers as Record<string, string>) };
  if (init.body && !(init.body instanceof FormData) && !headers['Content-Type']) headers['Content-Type'] = 'application/json';
  if (csrfToken) headers['X-XSRF-TOKEN'] = csrfToken;

  const res = await fetch(url, {
    credentials: 'same-origin',
    ...init,
    headers,
  });

  if (res.status === 401) {
    onUnauthorized?.();
  }

  const text = await res.text();
  let data: any = null;
  if (text) {
    try { data = JSON.parse(text); }
    catch { if (res.ok) throw new Error('El servidor devolvió una respuesta no válida.'); }
  }

  if (!res.ok) {
    const message = data?.message || data?.title || `Error ${res.status}`;
    const err = new Error(message) as Error & { code?: string; status?: number; current?: unknown };
    err.code = data?.code;
    err.status = res.status;
    err.current = data?.current;
    throw err;
  }
  return data as T;
}

export const api = {
  get: <T>(url: string) => request<T>(url),
  post: <T>(url: string, body?: unknown) => request<T>(url, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) }),
  upload: <T>(url: string, body: FormData, signal?: AbortSignal) => request<T>(url, { method: 'POST', body, signal }),
  put: <T>(url: string, body?: unknown) => request<T>(url, { method: 'PUT', body: body === undefined ? undefined : JSON.stringify(body) }),
  patch: <T>(url: string, body?: unknown) => request<T>(url, { method: 'PATCH', body: body === undefined ? undefined : JSON.stringify(body) }),
  del: <T>(url: string) => request<T>(url, { method: 'DELETE' }),
};

export function qs(params: Record<string, unknown>): string {
  const sp = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === null || v === '') continue;
    sp.append(k, String(v));
  }
  const s = sp.toString();
  return s ? `?${s}` : '';
}
