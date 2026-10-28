import { memo, useEffect, useRef, useState } from 'react';
import { Handle, Position, type NodeProps, type Node } from '@xyflow/react';
import type { DiagramTaskDto } from '../../types';
import { PRIORITY_LABELS } from '../../constants';

export interface TaskCardData extends Record<string, unknown> {
  task: DiagramTaskDto;
  states: { id: string; name: string; color?: string }[];
  onEdit: (id: string) => void;
  onCreateChild: (task: DiagramTaskDto) => void;
  onCenter: (id: string) => void;
  onPatch: (id: string, patch: { priority?: number; stateId?: string }) => void;
  onCollapse: (id: string) => void;
  onArchive: (task: DiagramTaskDto) => void;
  descendantCount: number; collapsed: boolean; filtering: boolean;
  context?: boolean; busy?: boolean; error?: string; incomingAccent?: string;
}

export type TaskCardNodeType = Node<TaskCardData>;
const CATEGORY = ['FUNCIÓN', 'TARJETA HIJA', 'SUBTAREA'];
const PRIORITY_COLOR = ['#A8B7D1', '#7C9CF5', '#EAC385', '#F5C400'];
const PRIORITY_ICON = ['↓', '▥', '⇈', '⚡'];

function TaskCardNode({ data, selected }: NodeProps<TaskCardNodeType>) {
  const { task, states, onEdit, onCreateChild, onCenter, onPatch } = data;
  const [menu, setMenu] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuButton = useRef<HTMLButtonElement>(null);
  const accent = /^#[0-9a-f]{6}$/i.test(task.accentColor || '') ? task.accentColor : '#9B8AFB';
  const surface = accent?.toUpperCase() === '#4B9EFF' ? '#19283B' : accent?.toUpperCase() === '#3AC5CA' ? '#162C33' : '#241D35';
  const priorityColor = PRIORITY_COLOR[task.priority] || '#A8B7D1';
  const stateColor = task.stateColor || '#A8B7D1';
  const progress = task.checklistProgress ?? task.progressPercent;
  const progressLabel = task.checklistProgress != null ? 'Checklist' : 'Progreso manual';

  useEffect(() => {
    if (!menu) return;
    const outside = (event: PointerEvent) => { if (!menuRef.current?.contains(event.target as HTMLElement)) setMenu(false); };
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape') { setMenu(false); menuButton.current?.focus(); } };
    document.addEventListener('pointerdown', outside);
    document.addEventListener('keydown', escape);
    return () => { document.removeEventListener('pointerdown', outside); document.removeEventListener('keydown', escape); };
  }, [menu]);
  const action = (fn: () => void) => { setMenu(false); fn(); };

  return (
    <div className={`task-card-node ${selected ? 'selected' : ''} ${data.context ? 'is-context' : ''} ${task.isArchived ? 'is-archived' : ''}`}
      style={{ borderColor: selected ? accent : `${accent}99`, background: surface }}>
      <Handle type="target" position={Position.Left} isConnectable={false} className="diagram-handle" style={{ background: data.incomingAccent || accent }} />
      <Handle type="source" position={Position.Right} isConnectable={false} className="diagram-handle" style={{ background: accent }} />
      <div className="tcn-head" title={task.canEdit ? 'Arrastra esta cabecera para mover la tarjeta' : undefined}>
        <div className="tcn-id"><span className="tcn-code">{task.code}</span><span aria-hidden="true">·</span><span className="tcn-cat">{CATEGORY[Math.min(task.depth, 2)]}</span>
          {data.context && <span className="tcn-context">Contexto</span>}{task.isArchived && <span className="badge">Archivada</span>}
        </div>
        <div className="tcn-menu-wrap nodrag nopan" ref={menuRef}>
          <button ref={menuButton} className="tcn-menu-btn nodrag nopan" aria-label={`Opciones de ${task.code}`} aria-expanded={menu} onClick={() => setMenu(!menu)}>⋯</button>
          {menu && <div className="tcn-menu nodrag nopan" role="group" aria-label={`Acciones de ${task.code}`}>
            <button onClick={() => action(() => onEdit(task.id))}>{task.canEdit && !task.isArchived ? 'Editar' : 'Ver detalle'}</button>
            {task.canCreateChild && !task.isArchived && <button onClick={() => action(() => onCreateChild(task))}>Crear hija</button>}
            <button onClick={() => action(() => onCenter(task.id))}>Centrar en pantalla</button>
            {data.descendantCount > 0 && <button disabled={data.filtering} title={data.filtering ? 'Quita los filtros para contraer ramas' : undefined} onClick={() => action(() => data.onCollapse(task.id))}>{data.collapsed ? 'Expandir' : 'Contraer'} {data.descendantCount} descendientes</button>}
            {task.canArchive && <button onClick={() => action(() => data.onArchive(task))}>{task.isArchived ? 'Restaurar' : 'Archivar'}</button>}
          </div>}
        </div>
      </div>
      <button className="tcn-title nodrag nopan" onClick={() => onEdit(task.id)} title={task.fullPath}>{task.displayTitle}</button>
      <div className="tcn-badges nodrag nopan">
        <select className="tcn-badge nodrag nopan" aria-label={`Prioridad de ${task.code}`} style={{ color: priorityColor, backgroundColor: `${priorityColor}20`, borderColor: `${priorityColor}66` }} value={task.priority}
          disabled={!task.canEdit || task.isArchived || data.busy} onChange={(event) => onPatch(task.id, { priority: Number(event.target.value) })}>
          {PRIORITY_LABELS.map((label, index) => <option key={label} value={index}>{PRIORITY_ICON[index]} {label}</option>)}
        </select>
        <select className="tcn-badge tcn-state nodrag nopan" aria-label={`Estado de ${task.code}`} style={{ color: stateColor, backgroundColor: `${stateColor}20`, borderColor: `${stateColor}66` }} value={task.stateId}
          disabled={!task.canChangeState || task.isArchived || data.busy} onChange={(event) => onPatch(task.id, { stateId: event.target.value })}>
          {!states.some((state) => state.id === task.stateId) && <option value={task.stateId}>● {task.stateName}</option>}
          {states.map((state) => <option key={state.id} value={state.id}>● {state.name}</option>)}
        </select>
      </div>
      {task.description && <div className="tcn-desc" title={task.description}>{task.description}</div>}
      <div className="tcn-note"><span className="tcn-note-label">Nota</span><div className="tcn-note-content" title={task.notes || undefined}>{task.notes || (task.canEdit && !task.isArchived ? <button className="tcn-add-note nodrag nopan" onClick={() => onEdit(task.id)}>Añadir nota</button> : <span className="tcn-note-empty">Sin nota</span>)}</div></div>
      {task.assigneeName && <div className="tcn-assignee">Responsable: {task.assigneeName}</div>}
      {progress != null && <div className="tcn-progress" aria-label={`${progressLabel}: ${progress}%`}>
        <div className="tcn-progress-bar" role="progressbar" aria-label={progressLabel} aria-valuenow={progress} aria-valuemin={0} aria-valuemax={100}><div className="tcn-progress-fill" style={{ width: `${Math.min(100, Math.max(0, progress))}%`, background: accent }} /></div>
        <span className="tcn-progress-text" title={progressLabel}>{progress}% completado</span>
      </div>}
      {data.descendantCount > 0 && <button className="tcn-collapse nodrag nopan" disabled={data.filtering} onClick={() => data.onCollapse(task.id)}>{data.collapsed && !data.filtering ? '▸' : '▾'} {data.descendantCount} descendientes{data.collapsed && !data.filtering ? ' ocultos' : ''}</button>}
      {data.error && <div className="tcn-error" role="alert">{data.error}</div>}
      {data.busy && <div className="muted small" role="status">Guardando cambio…</div>}
      {task.canCreateChild && !task.isArchived && <button className="tcn-child-btn nodrag nopan" onClick={() => onCreateChild(task)}>+ Crear hija</button>}
    </div>
  );
}

export default memo(TaskCardNode);
