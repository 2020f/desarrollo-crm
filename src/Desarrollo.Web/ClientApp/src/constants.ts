export const PRIORITY_LABELS = ['Baja', 'Media', 'Alta', 'Urgente'] as const;
export const PRIORITY_CLASS = ['', '', 'warn', 'error'] as const;

export const TYPE_LABELS = ['Funcionalidad', 'Historia de usuario', 'Tarea técnica', 'Error', 'Investigación', 'Deuda técnica'] as const;

export const CATEGORY_LABELS = ['Pendiente', 'En curso', 'Finalizado', 'Cancelado'] as const;

export const PROJECT_STATUS_LABELS = ['Propuesto', 'Activo', 'En pausa', 'Finalizado', 'Archivado'] as const;

export const TICKET_STATUS_LABELS = ['Recibido', 'En evaluación', 'Esperando información', 'Aceptado', 'En ejecución', 'Resuelto', 'Cerrado', 'Rechazado', 'Duplicado', 'Reabierto'] as const;

export const TICKET_CATEGORY_LABELS = ['Error', 'Mejora', 'Duda', 'Cambio'] as const;

export const SEVERITY_LABELS = ['Baja', 'Media', 'Alta', 'Crítica'] as const;

export const EVENT_TYPE_LABELS: Record<number, string> = {
  0: 'Tarea', 1: 'Reunión', 2: 'Hito', 3: 'Publicación',
};

export const NOTIF_EVENT_LABELS: Record<number, string> = {
  0: 'Asignación', 1: 'Mención', 2: 'Revisión solicitada', 3: 'Respuesta', 4: 'Vencimiento', 5: 'Bloqueo resuelto', 6: 'Comentario',
};

export function fmtDate(d?: string | null): string {
  if (!d) return '';
  const dt = new Date(d);
  if (Number.isNaN(dt.getTime())) return '';
  return dt.toLocaleDateString('es-DO', { day: '2-digit', month: 'short', year: 'numeric' });
}

export function fmtDateTime(d?: string | null): string {
  if (!d) return '';
  const dt = new Date(d);
  if (Number.isNaN(dt.getTime())) return '';
  return dt.toLocaleString('es-DO', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
}

export function initials(name?: string | null): string {
  if (!name) return '?';
  return name.trim().split(/\s+/).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('') || '?';
}

export function isOverdue(due?: string | null, category?: number): boolean {
  if (!due) return false;
  const d = new Date(due).getTime();
  if (Number.isNaN(d)) return false;
  return d < Date.now() && category !== 2 && category !== 3;
}
