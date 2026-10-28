import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
  ReactFlow, ReactFlowProvider, Background, BackgroundVariant,
  applyNodeChanges, useNodesInitialized, useReactFlow, useViewport,
  type Edge, type Node, type NodeChange, type Viewport,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { api } from '../api';
import type { ArchivePreviewDto, DiagramPositionDto, DiagramTaskDto, TaskDiagramDto, WorkItemDetailDto } from '../types';
import { PRIORITY_LABELS, TYPE_LABELS } from '../constants';
import TaskCardNode, { type TaskCardData } from '../components/diagram/TaskCardNode';
import TaskEditorPanel from '../components/TaskEditorPanel';
import { useNavigationGuard } from '../components/NavigationGuard';

type TaskNode = Node<TaskCardData>;
type EditorState = { task?: WorkItemDetailDto; parent?: DiagramTaskDto };
type ApiError = Error & { status?: number };
type PositionComparison = { taskId: string; code: string; localX: number; localY: number; storedX?: number; storedY?: number };
const ACCENTS = ['#9B8AFB', '#7C9CF5', '#57D6C7'];
const nodeTypes = { task: TaskCardNode };

function TaskDiagramInner({ projectId }: { projectId: string }) {
  const { register } = useNavigationGuard();
  const rf = useReactFlow<TaskNode>();
  const measured = useNodesInitialized();
  const viewport = useViewport();
  const [diagram, setDiagram] = useState<TaskDiagramDto | null>(null);
  const [nodes, setNodes] = useState<TaskNode[]>([]);
  const [edges, setEdges] = useState<Edge[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [loadError, setLoadError] = useState('');
  const [actionError, setActionError] = useState('');
  const [positionError, setPositionError] = useState('');
  const [positionConflict, setPositionConflict] = useState(false);
  const [positionStatus, setPositionStatus] = useState('');
  const [comparingPositions, setComparingPositions] = useState(false);
  const [positionComparison, setPositionComparison] = useState<PositionComparison[] | null>(null);
  const [cameraError, setCameraError] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [editorLoading, setEditorLoading] = useState(false);
  const [archiveTask, setArchiveTask] = useState<DiagramTaskDto | null>(null);
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [includeArchived, setIncludeArchived] = useState(false);
  const [showFilters, setShowFilters] = useState(false);
  const [priority, setPriority] = useState('');
  const [stateId, setStateId] = useState('');
  const [assigneeId, setAssigneeId] = useState('');
  const [type, setType] = useState('');
  const [taskErrors, setTaskErrors] = useState<Record<string, string>>({});
  const [busyTasks, setBusyTasks] = useState<Set<string>>(new Set());
  const [navigationBusy, setNavigationBusy] = useState(false);
  const tasksRef = useRef<DiagramTaskDto[]>([]);
  const nodesRef = useRef<TaskNode[]>([]);
  const positionsRef = useRef(new Map<string, DiagramPositionDto>());
  const dirtyRef = useRef(new Map<string, DiagramPositionDto>());
  const collapsedRef = useRef<Set<string>>(new Set());
  const initialViewport = useRef<Viewport | null>(null);
  const cameraReady = useRef(false);
  const initialLoaded = useRef(false);
  const pendingCenter = useRef<string | null>(null);
  const positionTimer = useRef<ReturnType<typeof setTimeout>>();
  const cameraTimer = useRef<ReturnType<typeof setTimeout>>();
  const savingPositions = useRef(false);
  const savingCamera = useRef(false);
  const positionWaiters = useRef<Array<() => void>>([]);
  const cameraWaiters = useRef<Array<() => void>>([]);
  const positionFailed = useRef(false);
  const cameraFailed = useRef(false);
  const navigating = useRef(false);
  const cameraRevision = useRef(0);
  const savedCameraRevision = useRef(0);
  const latestCamera = useRef<Viewport>({ x: 0, y: 0, zoom: 1 });
  const active = useRef(true);
  const loadSequence = useRef(0);
  const busyTaskIds = useRef(new Set<string>());
  const savePositionsRef = useRef<() => Promise<void>>(async () => {});
  const saveCameraRef = useRef<() => Promise<void>>(async () => {});
  const flushCanvasRef = useRef<() => Promise<boolean>>(async () => true);

  const load = useCallback(async () => {
    const sequence = ++loadSequence.current;
    if (initialLoaded.current) setRefreshing(true);
    try {
      const data = await api.get<TaskDiagramDto>('/api/projects/' + projectId + '/task-diagram?includeArchived=' + includeArchived);
      if (!active.current || sequence !== loadSequence.current) return;
      for (const task of data.tasks) {
        if (!dirtyRef.current.has(task.id)) positionsRef.current.set(task.id, { taskId: task.id, x: task.x, y: task.y, rowVersion: task.positionRowVersion });
      }
      const allowed = new Set(data.tasks.map((task) => task.id));
      for (const taskId of dirtyRef.current.keys()) if (!allowed.has(taskId)) dirtyRef.current.delete(taskId);
      if (!initialLoaded.current) {
        initialViewport.current = data.zoom > 0 ? { x: data.viewportX, y: data.viewportY, zoom: data.zoom } : null;
        const preference = new Set(data.collapsedTaskIds || []);
        collapsedRef.current = preference;
        setCollapsed(preference);
      }
      initialLoaded.current = true;
      tasksRef.current = data.tasks;
      setDiagram(data);
      setLoadError('');
    } catch (error) {
      if (active.current && sequence === loadSequence.current) setLoadError((error as Error).message);
    } finally {
      if (active.current && sequence === loadSequence.current) { setLoading(false); setRefreshing(false); }
    }
  }, [projectId, includeArchived]);

  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    active.current = true;
    return () => {
      active.current = false;
      clearTimeout(positionTimer.current); clearTimeout(cameraTimer.current);
    };
  }, []);
  useEffect(() => register({
    hasPendingChanges: () => dirtyRef.current.size > 0 || savingPositions.current || savingCamera.current
      || cameraRevision.current > savedCameraRevision.current,
    saveBeforeLeaving: async () => {
      navigating.current = true; setNavigationBusy(true);
      try { return await flushCanvasRef.current(); }
      finally { navigating.current = false; if (active.current) setNavigationBusy(false); }
    },
  }), [register, projectId]);

  const savePositions = useCallback(async () => {
    if (savingPositions.current) return new Promise<void>((resolve) => positionWaiters.current.push(resolve));
    if (!dirtyRef.current.size) return;
    savingPositions.current = true;
    const positions = [...dirtyRef.current.values()].map((position) => ({ ...position }));
    if (active.current) setPositionStatus('Guardando posiciones…');
    let succeeded = false;
    try {
      const result = await api.patch<{ positions: DiagramPositionDto[] }>('/api/projects/' + projectId + '/task-diagram/positions', { positions });
      for (const saved of result.positions) {
        const pending = dirtyRef.current.get(saved.taskId);
        const sent = positions.find((position) => position.taskId === saved.taskId);
        if (pending && sent && pending.x === sent.x && pending.y === sent.y) dirtyRef.current.delete(saved.taskId);
        else if (pending) dirtyRef.current.set(saved.taskId, { ...pending, rowVersion: saved.rowVersion });
        const local = positionsRef.current.get(saved.taskId);
        if (local) positionsRef.current.set(saved.taskId, { ...local, rowVersion: saved.rowVersion });
      }
      succeeded = true;
      positionFailed.current = false;
      if (active.current) { setPositionError(''); setPositionConflict(false); setPositionStatus(dirtyRef.current.size ? 'Hay posiciones por guardar' : 'Posiciones guardadas'); }
    } catch (error) {
      positionFailed.current = true;
      if (active.current) {
        const conflict = (error as ApiError).status === 409;
        setPositionConflict(conflict);
        setPositionError(conflict ? 'Otra persona cambió la distribución. Tus posiciones se conservan; puedes revisarlas y reintentar tu distribución.' : 'No se guardaron las posiciones: ' + (error as Error).message);
        setPositionStatus('Posiciones sin guardar');
      }
    } finally {
      savingPositions.current = false;
      for (const resolve of positionWaiters.current.splice(0)) resolve();
      if (succeeded && dirtyRef.current.size && active.current) positionTimer.current = setTimeout(() => void savePositionsRef.current(), 400);
    }
  }, [projectId]);
  savePositionsRef.current = savePositions;

  const retryPositions = async () => {
    if (positionConflict) {
      try {
        const latest = await api.get<TaskDiagramDto>('/api/projects/' + projectId + '/task-diagram?includeArchived=' + includeArchived);
        const byId = new Map(latest.tasks.map((task) => [task.id, task]));
        for (const [taskId, position] of dirtyRef.current) {
          const task = byId.get(taskId);
          if (task?.canEdit && !task.isArchived) {
            const next = { ...position, rowVersion: task.positionRowVersion };
            dirtyRef.current.set(taskId, next);
            positionsRef.current.set(taskId, next);
          } else dirtyRef.current.delete(taskId);
        }
        if (!dirtyRef.current.size) { setPositionError('Las tarjetas pendientes ya no se pueden mover. Actualiza el mapa para revisar sus permisos.'); return; }
      } catch (error) { setPositionError((error as Error).message); return; }
    }
    await savePositions();
  };

  const comparePositions = async () => {
    setComparingPositions(true);
    try {
      const latest = await api.get<TaskDiagramDto>('/api/projects/' + projectId + '/task-diagram?includeArchived=' + includeArchived);
      const saved = new Map(latest.tasks.map((task) => [task.id, task]));
      setPositionComparison([...dirtyRef.current.values()].map((position) => {
        const task = saved.get(position.taskId);
        return { taskId: position.taskId, code: task?.code || tasksRef.current.find((item) => item.id === position.taskId)?.code || 'Tarjeta',
          localX: position.x, localY: position.y, storedX: task?.x, storedY: task?.y };
      }));
    } catch (error) {
      setActionError('No se pudo comparar la distribución: ' + (error as Error).message);
    } finally { setComparingPositions(false); }
  };

  const markPosition = useCallback((taskId: string, position: { x: number; y: number }) => {
    if (navigating.current) return;
    const task = tasksRef.current.find((item) => item.id === taskId);
    if (!task?.canEdit || task.isArchived) return;
    const stored = positionsRef.current.get(taskId);
    const next = { taskId, ...position, rowVersion: stored?.rowVersion ?? task.positionRowVersion };
    positionsRef.current.set(taskId, next);
    dirtyRef.current.set(taskId, next);
    setPositionStatus('Hay posiciones por guardar');
    clearTimeout(positionTimer.current);
    positionTimer.current = setTimeout(() => void savePositionsRef.current(), 500);
  }, []);

  const changeNodes = useCallback((changes: NodeChange<TaskNode>[]) => {
    setNodes((previous) => {
      // Selection belongs to selectedId. React Flow can report the preceding
      // node selection while a save reloads/rebuilds the node collection.
      const next = applyNodeChanges(changes.filter((change) => change.type !== 'select'), previous);
      nodesRef.current = next;
      return next;
    });
    for (const change of changes) if (change.type === 'position' && change.position && !change.dragging) markPosition(change.id, change.position);
  }, [markPosition]);

  const saveCamera = useCallback(async () => {
    if (!cameraReady.current) return;
    if (savingCamera.current) return new Promise<void>((resolve) => cameraWaiters.current.push(resolve));
    if (cameraRevision.current <= savedCameraRevision.current) return;
    savingCamera.current = true;
    const revision = cameraRevision.current;
    const current = latestCamera.current;
    try {
      await api.put('/api/projects/' + projectId + '/task-diagram/me/viewport', {
        viewportX: current.x, viewportY: current.y, zoom: current.zoom, collapsedTaskIds: [...collapsedRef.current],
      });
      savedCameraRevision.current = revision;
      cameraFailed.current = false;
      if (active.current) setCameraError('');
    } catch (error) {
      cameraFailed.current = true;
      if (active.current) setCameraError('No se guardó tu vista personal: ' + (error as Error).message);
    } finally {
      savingCamera.current = false;
      for (const resolve of cameraWaiters.current.splice(0)) resolve();
      if (!cameraFailed.current && cameraRevision.current > savedCameraRevision.current && active.current)
        cameraTimer.current = setTimeout(() => void saveCameraRef.current(), 400);
    }
  }, [projectId]);
  saveCameraRef.current = saveCamera;

  flushCanvasRef.current = async () => {
    clearTimeout(positionTimer.current); clearTimeout(cameraTimer.current);
    while (dirtyRef.current.size) {
      await savePositionsRef.current();
      clearTimeout(positionTimer.current);
      if (positionFailed.current) return false;
    }
    while (savingCamera.current || cameraRevision.current > savedCameraRevision.current) {
      if (!cameraReady.current) return false;
      await saveCameraRef.current();
      clearTimeout(cameraTimer.current);
      if (cameraFailed.current) return false;
    }
    return true;
  };

  const scheduleCamera = useCallback(() => {
    cameraRevision.current++;
    clearTimeout(cameraTimer.current);
    cameraTimer.current = setTimeout(() => void saveCameraRef.current(), 650);
  }, []);

  const toggleCollapsed = useCallback((taskId: string) => {
    if (navigating.current) return;
    const next = new Set(collapsedRef.current);
    if (next.has(taskId)) next.delete(taskId); else next.add(taskId);
    collapsedRef.current = next;
    setCollapsed(next);
    scheduleCamera();
  }, [scheduleCamera]);

  const centerOn = useCallback((taskId: string) => {
    const node = rf.getNode(taskId);
    if (node) void rf.setCenter(node.position.x + (node.measured?.width || 300) / 2, node.position.y + (node.measured?.height || 260) / 2, { zoom: Math.max(0.6, Math.min(rf.getZoom(), 1.2)), duration: 300 });
  }, [rf]);

  const editTask = useCallback(async (taskId: string) => {
    setEditorLoading(true);
    setActionError('');
    try { setEditor({ task: await api.get<WorkItemDetailDto>('/api/workitems/' + taskId) }); }
    catch (error) { setActionError((error as Error).message); }
    finally { setEditorLoading(false); }
  }, []);

  const createChild = useCallback((parent: DiagramTaskDto) => setEditor({ parent }), []);
  const openArchive = useCallback((task: DiagramTaskDto) => setArchiveTask(task), []);
  const patchTask = useCallback(async (taskId: string, patch: { priority?: number; stateId?: string }) => {
    if (navigating.current) return;
    if (busyTaskIds.current.has(taskId)) return;
    const task = tasksRef.current.find((item) => item.id === taskId);
    if (!task) return;
    busyTaskIds.current.add(taskId);
    setBusyTasks(new Set(busyTaskIds.current));
    setTaskErrors((previous) => ({ ...previous, [taskId]: '' }));
    try {
      if (patch.stateId) await api.post<WorkItemDetailDto>('/api/workitems/' + taskId + '/transition', { toStateId: patch.stateId, rowVersion: task.rowVersion });
      else await api.patch<WorkItemDetailDto>('/api/workitems/' + taskId, { ...patch, rowVersion: task.rowVersion });
      await load();
    } catch (error) {
      const message = (error as ApiError).status === 409 ? 'La tarea cambió. Elige de nuevo el valor después de actualizar el mapa.' : (error as Error).message;
      setTaskErrors((previous) => ({ ...previous, [taskId]: message }));
    } finally {
      busyTaskIds.current.delete(taskId);
      setBusyTasks(new Set(busyTaskIds.current));
    }
  }, [load]);

  const hasFilters = priority !== '' || stateId !== '' || assigneeId !== '' || type !== '';
  const clearFilters = () => { setPriority(''); setStateId(''); setAssigneeId(''); setType(''); };
  const visibility = useMemo(() => {
    const tasks = diagram?.tasks || [];
    const byId = new Map(tasks.map((task) => [task.id, task]));
    const children = new Map<string, string[]>();
    for (const task of tasks) if (task.parentTaskId && byId.has(task.parentTaskId)) children.set(task.parentTaskId, [...(children.get(task.parentTaskId) || []), task.id]);
    const descendants = (taskId: string) => {
      const seen = new Set<string>([taskId]);
      const queue = [...(children.get(taskId) || [])];
      const found = new Set<string>();
      while (queue.length) {
        const current = queue.pop()!;
        if (seen.has(current)) continue;
        seen.add(current); found.add(current);
        queue.push(...(children.get(current) || []));
      }
      return found;
    };
    const counts = new Map(tasks.map((task) => [task.id, descendants(task.id).size]));
    const matches = new Set(tasks.filter((task) =>
      (priority === '' || task.priority === Number(priority)) && (stateId === '' || task.stateId === stateId) &&
      (assigneeId === '' || (assigneeId === 'none' ? !task.assigneeId : task.assigneeId === assigneeId)) &&
      (type === '' || task.type === Number(type))).map((task) => task.id));
    const visible = new Set<string>();
    if (hasFilters) {
      for (const taskId of matches) {
        const visited = new Set<string>();
        let current = byId.get(taskId);
        while (current && !visited.has(current.id)) {
          visited.add(current.id); visible.add(current.id);
          current = current.parentTaskId ? byId.get(current.parentTaskId) : undefined;
        }
      }
    } else {
      const hidden = new Set<string>();
      for (const taskId of collapsed) for (const child of descendants(taskId)) hidden.add(child);
      for (const task of tasks) if (!hidden.has(task.id)) visible.add(task.id);
    }
    return { visible, matches, counts, byId };
  }, [diagram, priority, stateId, assigneeId, type, hasFilters, collapsed]);

  useEffect(() => {
    if (!diagram) return;
    const previous = new Map(nodesRef.current.map((node) => [node.id, node]));
    const next: TaskNode[] = diagram.tasks.filter((task) => visibility.visible.has(task.id)).map((task) => ({
      ...previous.get(task.id),
      id: task.id, type: 'task', position: positionsRef.current.get(task.id) || { x: task.x, y: task.y },
      dragHandle: '.tcn-head', draggable: task.canEdit && !task.isArchived,
      selected: task.id === selectedId, deletable: false, connectable: false,
      ariaLabel: task.code + ', ' + task.displayTitle + ', ' + task.stateName,
      data: {
        task, states: diagram.states, onEdit: editTask, onCreateChild: createChild, onCenter: centerOn, onPatch: patchTask,
        onCollapse: toggleCollapsed, onArchive: openArchive, descendantCount: visibility.counts.get(task.id) || 0,
        collapsed: collapsed.has(task.id), filtering: hasFilters, context: hasFilters && !visibility.matches.has(task.id),
        incomingAccent: task.parentTaskId && visibility.byId.get(task.parentTaskId)?.depth === 0 ? visibility.byId.get(task.parentTaskId)?.accentColor : task.accentColor,
        busy: busyTasks.has(task.id), error: taskErrors[task.id],
      },
    }));
    nodesRef.current = next;
    setNodes(next);
    setEdges(diagram.tasks.filter((task) => task.parentTaskId && visibility.visible.has(task.id) && visibility.visible.has(task.parentTaskId)).map((task) => {
      const parent = visibility.byId.get(task.parentTaskId!)!;
      return {
        id: 'hierarchy-' + task.id, source: parent.id, target: task.id, type: 'default', selectable: false, deletable: false,
        label: parent.depth === 0 ? 'Hija' : undefined,
        labelStyle: { fill: '#A8B7D1', fontSize: 12, fontWeight: 600 }, labelBgStyle: { fill: '#0D141E', fillOpacity: 0.95 },
        style: { stroke: (parent.depth === 0 ? parent.accentColor : task.accentColor) || '#9B8AFB', strokeWidth: 2.3 },
      };
    }));
  }, [diagram, visibility, selectedId, collapsed, hasFilters, busyTasks, taskErrors, editTask, createChild, centerOn, patchTask, toggleCollapsed, openArchive]);

  useEffect(() => {
    if (!diagram || loading || nodes.length !== visibility.visible.size || (nodes.length > 0 && !measured)) return;
    if (!cameraReady.current) {
      cameraReady.current = true;
      if (initialViewport.current) { latestCamera.current = initialViewport.current; void rf.setViewport(initialViewport.current); }
      else if (nodes.length) void rf.fitView({ padding: 0.2 });
    }
    if (pendingCenter.current && nodes.some((node) => node.id === pendingCenter.current)) {
      const taskId = pendingCenter.current;
      pendingCenter.current = null;
      centerOn(taskId);
    }
  }, [diagram, loading, measured, nodes, visibility.visible.size, rf, centerOn]);

  const savedTask = async (task: WorkItemDetailDto) => {
    const created = !tasksRef.current.some((item) => item.id === task.id);
    const parentPosition = task.parentTaskId ? positionsRef.current.get(task.parentTaskId) : undefined;
    let createdPosition: { x: number; y: number } | undefined;
    if (created && parentPosition) {
      const siblings = tasksRef.current.filter((item) => item.parentTaskId === task.parentTaskId);
      const lastBottom = siblings.reduce((bottom, item) => {
        const position = positionsRef.current.get(item.id);
        const height = rf.getNode(item.id)?.measured?.height || 460;
        return position ? Math.max(bottom, position.y + height + 44) : bottom;
      }, parentPosition.y);
      const candidate = { x: parentPosition.x + 440, y: lastBottom };
      const authorizedIds = new Set(tasksRef.current.map((item) => item.id));
      const occupied = [...positionsRef.current.values()].filter((position) => position.taskId !== task.id && authorizedIds.has(position.taskId)).map((position) => ({
        ...position,
        width: rf.getNode(position.taskId)?.measured?.width || 300,
        height: rf.getNode(position.taskId)?.measured?.height || 460,
      }));
      const margin = 44;
      let collisions = occupied.filter((position) => candidate.x < position.x + position.width + margin && candidate.x + 300 + margin > position.x
        && candidate.y < position.y + position.height + margin && candidate.y + 460 + margin > position.y);
      while (collisions.length) {
        candidate.y = Math.max(...collisions.map((position) => position.y + position.height + margin));
        collisions = occupied.filter((position) => candidate.x < position.x + position.width + margin && candidate.x + 300 + margin > position.x
          && candidate.y < position.y + position.height + margin && candidate.y + 460 + margin > position.y);
      }
      createdPosition = candidate;
    }
    setEditor(null);
    clearFilters();
    const nextCollapsed = new Set(collapsedRef.current);
    let parent = task.parentTaskId;
    const visited = new Set<string>();
    while (parent && !visited.has(parent)) {
      visited.add(parent); nextCollapsed.delete(parent);
      parent = tasksRef.current.find((item) => item.id === parent)?.parentTaskId;
    }
    collapsedRef.current = nextCollapsed;
    setCollapsed(nextCollapsed);
    setSelectedId(task.id);
    pendingCenter.current = task.id;
    scheduleCamera();
    await load();
    if (createdPosition) {
      markPosition(task.id, createdPosition);
      setNodes((previous) => {
        const next = previous.map((item) => item.id === task.id ? { ...item, position: createdPosition! } : item);
        nodesRef.current = next;
        return next;
      });
    }
  };

  const nudge = (dx: number, dy: number) => {
    if (navigating.current) return;
    if (!selectedId) return;
    const node = rf.getNode(selectedId);
    if (!node?.draggable) return;
    const position = { x: node.position.x + dx, y: node.position.y + dy };
    markPosition(selectedId, position);
    setNodes((previous) => {
      const next = previous.map((item) => item.id === selectedId ? { ...item, position } : item);
      nodesRef.current = next;
      return next;
    });
  };

  const selectedTask = diagram?.tasks.find((task) => task.id === selectedId);
  const assignees = [...new Map((diagram?.tasks || []).filter((task) => task.assigneeId).map((task) => [task.assigneeId!, task.assigneeName || 'Miembro del proyecto'])).entries()];
  const canCreate = diagram?.permissions.includes(2);

  return (
    <div className={'diagram-shell' + (navigationBusy ? ' is-navigating' : '')} aria-busy={navigationBusy}>
      <div className="diagram-toolbar">
        <div>
          <h1 className="diagram-title">Mapa de tareas</h1>
          <div className="muted small">Organiza funciones, conecta tareas y sigue el avance</div>
          {diagram && <div className="diagram-project">{diagram.projectName}</div>}
          <nav className="view-tabs" aria-label="Vistas del proyecto">
            <Link className="view-tab" to={'/projects/' + projectId + '/board'}>Tablero</Link>
            <Link className="view-tab" to={'/projects/' + projectId + '/table'}>Lista</Link>
            <span className="view-tab active" aria-current="page">Diagrama</span>
          </nav>
        </div>
        <div className="diagram-toolbar-actions">
          <button className="btn" onClick={() => void load()} disabled={loading || refreshing}>{refreshing ? 'Actualizando…' : 'Actualizar'}</button>
          <button className="btn" aria-expanded={showFilters} aria-controls="diagram-filters" onClick={() => setShowFilters(!showFilters)}>☷ Filtrar{hasFilters ? ' · activo' : ''}</button>
          {canCreate && <button className="btn primary" onClick={() => setEditor({})}>+ Nueva tarjeta</button>}
        </div>
      </div>
      {showFilters && <div id="diagram-filters" className="diagram-filters">
        <label>Prioridad<select aria-label="Prioridad" className="input" value={priority} onChange={(event) => setPriority(event.target.value)}><option value="">Todas</option>{PRIORITY_LABELS.map((label, index) => <option key={label} value={index}>{label}</option>)}</select></label>
        <label>Estado<select aria-label="Estado" className="input" value={stateId} onChange={(event) => setStateId(event.target.value)}><option value="">Todos</option>{diagram?.states.map((state) => <option key={state.id} value={state.id}>{state.name}</option>)}</select></label>
        <label>Responsable<select aria-label="Responsable" className="input" value={assigneeId} onChange={(event) => setAssigneeId(event.target.value)}><option value="">Todos</option><option value="none">Sin asignar</option>{assignees.map(([key, name]) => <option key={key} value={key}>{name}</option>)}</select></label>
        <label>Tipo<select aria-label="Tipo" className="input" value={type} onChange={(event) => setType(event.target.value)}><option value="">Todos</option>{TYPE_LABELS.map((label, index) => <option key={label} value={index}>{label}</option>)}</select></label>
        <label className="diagram-archive-toggle"><input type="checkbox" checked={includeArchived} onChange={(event) => setIncludeArchived(event.target.checked)} /> Incluir archivadas</label>
        {hasFilters && <button className="btn sm" onClick={clearFilters}>Limpiar filtros</button>}
      </div>}
      <div className="diagram-status" aria-live="polite">
        <span>{navigationBusy ? 'Guardando cambios antes de abrir la vista…' : diagram ? visibility.visible.size + ' de ' + diagram.tasks.length + ' tarjetas' : ''}{positionStatus ? ' · ' + positionStatus : ''}</span>
        {hasFilters && <span>Los antecesores autorizados aparecen como contexto. Las ramas coincidentes se muestran expandidas.</span>}
      </div>
      {loadError && <div className="alert error" role="alert">{loadError} <button className="btn sm" onClick={() => void load()}>Reintentar carga</button></div>}
      {actionError && <div className="alert error" role="alert">{actionError}</div>}
      {positionError && <div className="alert error" role="alert">{positionError} {positionConflict && <button className="btn sm" disabled={comparingPositions} onClick={() => void comparePositions()}>{comparingPositions ? 'Comparando…' : 'Comparar con distribución guardada'}</button>}<button className="btn sm" onClick={() => void retryPositions()}>{positionConflict ? 'Reintentar mi distribución' : 'Reintentar guardado'}</button></div>}
      {cameraError && <div className="alert error" role="alert">{cameraError} <button className="btn sm" onClick={() => void saveCamera()}>Reintentar vista</button></div>}
      {editorLoading && <div className="muted small" role="status">Cargando detalle…</div>}
      <div className="diagram-canvas" aria-label="Mapa de tareas del proyecto" onKeyDownCapture={(event) => {
        const target = event.target as HTMLElement;
        if (!target.classList.contains('react-flow__node')) return;
        if (event.key === 'Enter' || event.key === ' ') setSelectedId(target.dataset.id || null);
        else if (event.key === 'Escape') setSelectedId(null);
      }}>
        <ReactFlow<TaskNode> nodes={nodes} edges={edges} nodeTypes={nodeTypes} onNodesChange={changeNodes}
          onNodeClick={(_, node) => setSelectedId(node.id)} onPaneClick={() => setSelectedId(null)}
          onNodeDragStart={(_, node) => setSelectedId(node.id)}
          onNodeDragStop={(_, node) => markPosition(node.id, node.position)}
          onMoveEnd={(_, next) => { latestCamera.current = next; if (cameraReady.current) scheduleCamera(); }}
          deleteKeyCode={null} nodesConnectable={false} edgesReconnectable={false} selectionOnDrag={false}
          minZoom={0.2} maxZoom={3} fitView={false} proOptions={{ hideAttribution: true }}>
          <Background variant={BackgroundVariant.Dots} gap={22} size={1.2} color="#2A3550" bgColor="#0D141E" />
        </ReactFlow>
        {(loading || (!diagram && !loadError)) && <div className="diagram-empty" role="status">Cargando mapa…</div>}
        {!loading && diagram && nodes.length === 0 && <div className="diagram-empty"><strong>{hasFilters ? 'Ninguna tarjeta coincide con estos filtros' : 'Este proyecto todavía no tiene tarjetas'}</strong><p>{hasFilters ? 'Prueba otro filtro para recuperar las tarjetas.' : 'Crea una función y desglósala en tareas hijas.'}</p>{hasFilters ? <button className="btn" onClick={clearFilters}>Limpiar filtros</button> : canCreate ? <button className="btn primary" onClick={() => setEditor({})}>+ Nueva tarjeta</button> : null}</div>}
        <div className="diagram-legend" aria-label="Categorías de tarjetas"><span><i style={{ background: ACCENTS[0] }} /> Función</span><span><i style={{ background: ACCENTS[1] }} /> Tarea</span><span><i style={{ background: ACCENTS[2] }} /> Subtarea</span></div>
        <div className="diagram-zoom" aria-label="Controles del lienzo">
          <button onClick={() => void rf.zoomOut()} aria-label="Alejar">−</button><span className="diagram-zoom-pct">{Math.round(viewport.zoom * 100)}%</span><button onClick={() => void rf.zoomIn()} aria-label="Acercar">+</button>
          <button className="diagram-fit" disabled={!nodes.length} onClick={() => void rf.fitView({ padding: 0.2, duration: 300 })}>Ajustar a pantalla</button>
        </div>
      </div>
      <div className="diagram-selection">
        {selectedTask ? <><span><strong>{selectedTask.code}</strong> · {selectedTask.title}</span><button className="btn sm" onClick={() => void editTask(selectedTask.id)}>Abrir detalle</button><button className="btn sm" onClick={() => centerOn(selectedTask.id)}>Centrar</button>
          {selectedTask.canEdit && !selectedTask.isArchived && <div className="diagram-nudge" role="group" aria-label="Mover tarjeta seleccionada"><span>Mover:</span><button className="btn sm" aria-label="Mover a la izquierda" onClick={() => nudge(-30, 0)}>←</button><button className="btn sm" aria-label="Mover arriba" onClick={() => nudge(0, -30)}>↑</button><button className="btn sm" aria-label="Mover abajo" onClick={() => nudge(0, 30)}>↓</button><button className="btn sm" aria-label="Mover a la derecha" onClick={() => nudge(30, 0)}>→</button></div>}</>
          : <span className="muted small">Selecciona una tarjeta. Usa Tab para acceder a los controles; arrastra solo la cabecera para moverla.</span>}
      </div>
      {editor && diagram && <TaskEditorPanel projectId={projectId} task={editor.task} parent={editor.parent} states={diagram.states} onClose={() => setEditor(null)} onSaved={(task) => void savedTask(task)} />}
      {archiveTask && <ArchiveDialog task={archiveTask} onClose={() => setArchiveTask(null)} onSaved={() => { setArchiveTask(null); void load(); }} />}
      {positionComparison && <PositionComparisonDialog rows={positionComparison} onClose={() => setPositionComparison(null)} />}
    </div>
  );
}

function PositionComparisonDialog({ rows, onClose }: { rows: PositionComparison[]; onClose: () => void }) {
  const close = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    close.current?.focus();
    return () => { if (previous?.isConnected) previous.focus(); };
  }, []);
  const coordinates = (x?: number, y?: number) => x == null || y == null ? 'No disponible' : 'X ' + x.toFixed(1) + ' · Y ' + y.toFixed(1);
  return <div className="modal-backdrop"><div className="modal card diagram-position-comparison" role="dialog" aria-modal="true" aria-labelledby="position-comparison-title"
    onKeyDown={(event) => { if (event.key === 'Escape') onClose(); if (event.key === 'Tab') { event.preventDefault(); close.current?.focus(); } }}>
    <h2 id="position-comparison-title">Comparar distribuciones</h2>
    <p className="muted small">Esta comparación conserva tu distribución y sus versiones. Para guardar tus posiciones, cierra y pulsa «Reintentar mi distribución».</p>
    <div className="diagram-comparison-scroll"><table className="data-table"><thead><tr><th>Tarjeta</th><th>Guardada actualmente</th><th>Tu posición pendiente</th></tr></thead><tbody>{rows.map((row) => <tr key={row.taskId}><th scope="row">{row.code}</th><td>{coordinates(row.storedX, row.storedY)}</td><td>{coordinates(row.localX, row.localY)}</td></tr>)}</tbody></table></div>
    {!rows.length && <p>No quedan posiciones pendientes.</p>}
    <div className="row" style={{ justifyContent: 'flex-end', marginTop: 16 }}><button ref={close} className="btn primary" onClick={onClose}>Cerrar comparación</button></div>
  </div></div>;
}

function ArchiveDialog({ task, onClose, onSaved }: { task: DiagramTaskDto; onClose: () => void; onSaved: () => void }) {
  const [preview, setPreview] = useState<ArchivePreviewDto | null>(null);
  const [includeDescendants, setIncludeDescendants] = useState(false);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [refreshing, setRefreshing] = useState(false);
  const [rowVersion, setRowVersion] = useState(task.rowVersion);
  const [conflict, setConflict] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useRef<HTMLDivElement>(null);
  const loadPreview = useCallback(async () => {
    setRefreshing(true);
    try {
      const result = await api.get<ArchivePreviewDto>('/api/workitems/' + task.id + '/archive-preview?restore=' + task.isArchived);
      setPreview(result); setError('');
    } catch (err) { setError((err as Error).message); }
    finally { setRefreshing(false); }
  }, [task]);
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    void loadPreview(); closeRef.current?.focus();
    return () => { if (previous?.isConnected) previous.focus(); };
  }, [loadPreview]);
  const reloadConflict = async () => {
    try {
      const latest = await api.get<WorkItemDetailDto>('/api/workitems/' + task.id);
      setRowVersion(latest.rowVersion);
      await loadPreview();
      setConflict(false);
    } catch (err) { setError((err as Error).message); }
  };
  const submit = async () => {
    if (!preview || busy || conflict) return;
    setBusy(true); setError('');
    try {
      await api.post('/api/workitems/' + task.id + (task.isArchived ? '/restore' : '/archive'), { rowVersion, includeDescendants });
      onSaved();
    } catch (err) {
      setError((err as ApiError).status === 409 ? 'La rama cambió. Revisa el alcance actualizado antes de confirmar; tu elección se conserva.' : (err as Error).message);
      setConflict((err as ApiError).status === 409);
    } finally { setBusy(false); }
  };
  return <div className="modal-backdrop">
    <div className="modal card diagram-archive-dialog" role="dialog" aria-modal="true" aria-labelledby="archive-title" ref={dialogRef}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && !busy) onClose();
        if (event.key === 'Tab') {
          const controls = dialogRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled)');
          const first = controls?.[0], last = controls?.[controls.length - 1];
          if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
          else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
        }
      }}>
      <h2 id="archive-title">{task.isArchived ? 'Restaurar' : 'Archivar'} {task.code}</h2>
      <p>{task.title}</p>
      {refreshing && <p role="status">Calculando alcance…</p>}
      {error && <div className="alert error" role="alert">{error}<button className="btn sm" onClick={() => void (conflict ? reloadConflict() : loadPreview())}>{conflict ? 'Revisar alcance actualizado' : 'Reintentar'}</button></div>}
      {preview && <><p>Esta operación afecta a {preview.count} {preview.count === 1 ? 'tarjeta' : 'tarjetas'}.</p><ul className="diagram-archive-list">{preview.tasks.map((item) => <li key={item.id}><strong>{item.code}</strong> · {item.title}</li>)}</ul>
        {preview.count > 1 && <><label className="diagram-archive-toggle"><input type="checkbox" checked={includeDescendants} onChange={(event) => setIncludeDescendants(event.target.checked)} /> {task.isArchived ? 'Restaurar' : 'Archivar'} también todos los descendientes indicados</label><p className="muted small">Para actuar solo sobre la madre, reasigna primero sus hijas desde el detalle.</p></>}
        {!preview.canArchive && <div className="alert error">No tienes permiso sobre todas las tarjetas de esta rama.</div>}</>}
      <div className="row" style={{ justifyContent: 'flex-end', marginTop: 18 }}><button ref={closeRef} className="btn" disabled={busy} onClick={onClose}>Cancelar</button><button className="btn primary" disabled={busy || refreshing || conflict || !preview?.canArchive || (preview.count > 1 && !includeDescendants)} onClick={() => void submit()}>{busy ? 'Guardando…' : task.isArchived ? 'Restaurar' : 'Archivar'}</button></div>
    </div>
  </div>;
}

export default function TaskDiagramPage() {
  const { id } = useParams<{ id: string }>();
  if (!id) return <div className="alert error">Proyecto no encontrado.</div>;
  return <ReactFlowProvider key={id}><TaskDiagramInner projectId={id} /></ReactFlowProvider>;
}
