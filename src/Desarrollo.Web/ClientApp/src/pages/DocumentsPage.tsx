import { useCallback, useEffect, useRef, useState } from 'react';
import type { JSONContent } from '@tiptap/core';
import { api } from '../api';
import type { DocumentDto, MemberDto, Paged, ProjectDto } from '../types';
import { fmtDateTime } from '../constants';
import { useAuth } from '../App';
import { useNavigationGuard } from '../components/NavigationGuard';
import DocumentRichEditor, { documentContent, EMPTY_DOCUMENT } from '../components/DocumentRichEditor';
import './documents.css';

interface Draft { title: string; content: JSONContent; parentId: string | null; visibility: 0 | 1 | 2; readerIds: string[]; }
const VISIBILITY = ['Todas las personas del proyecto', 'Personas seleccionadas', 'Solo yo'];
function toDraft(document: DocumentDto): Draft { return { title: document.title, content: documentContent(document.content, document.contentFormat), parentId: document.parentId || null, visibility: document.visibility ?? 2, readerIds: document.readerIds || [] }; }
function signature(draft: Draft) { return JSON.stringify({ ...draft, readerIds: [...draft.readerIds].sort() }); }
function readableContent(document: DocumentDto) {
  try {
    const text = (node: JSONContent): string => node.type === 'text' ? node.text || '' : (node.content || []).map(text).join(node.type === 'paragraph' || node.type === 'heading' ? '' : '\n');
    return text(documentContent(document.content, document.contentFormat));
  } catch { return 'No se pudo mostrar esta versión. Actualiza el documento antes de reintentar.'; }
}
function orderedDocuments(documents: DocumentDto[]) {
  const result: { document: DocumentDto; depth: number }[] = [];
  const seen = new Set<string>(), ids = new Set(documents.map((document) => document.id));
  const visit = (document: DocumentDto, depth: number) => {
    if (seen.has(document.id)) return;
    seen.add(document.id); result.push({ document, depth });
    for (const child of documents.filter((candidate) => candidate.parentId === document.id)) visit(child, depth + 1);
  };
  for (const document of documents.filter((candidate) => !candidate.parentId || !ids.has(candidate.parentId))) visit(document, 0);
  for (const document of documents) visit(document, 0);
  return result;
}

export default function DocumentsPage() {
  const { user } = useAuth();
  const { register } = useNavigationGuard();
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [projectId, setProjectId] = useState('');
  const [documents, setDocuments] = useState<DocumentDto[]>([]);
  const [members, setMembers] = useState<MemberDto[]>([]);
  const [base, setBase] = useState<DocumentDto | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [remote, setRemote] = useState<DocumentDto | null>(null);
  const [loadingList, setLoadingList] = useState(false);
  const [loadingDocument, setLoadingDocument] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [listError, setListError] = useState('');
  const [projectError, setProjectError] = useState('');
  const [memberError, setMemberError] = useState('');
  const [savedMessage, setSavedMessage] = useState('');
  const [editorKey, setEditorKey] = useState(0);
  const mounted = useRef(true), projectRef = useRef('');
  const baseRef = useRef<DocumentDto | null>(null), draftRef = useRef<Draft | null>(null), remoteRef = useRef<DocumentDto | null>(null);
  const baseline = useRef('');
  const savingRef = useRef(false), listSequence = useRef(0), documentSequence = useRef(0);
  const saveAttempt = useRef<Promise<boolean> | null>(null);
  const flushRef = useRef<() => Promise<boolean>>(async () => true);
  const contentErrorRef = useRef('');
  const titleInput = useRef<HTMLInputElement>(null);
  const dirty = () => !!draftRef.current && signature(draftRef.current) !== baseline.current;

  useEffect(() => { mounted.current = true; return () => { mounted.current = false; listSequence.current++; documentSequence.current++; }; }, []);
  const loadProjects = useCallback(async () => {
    try {
      const result = await api.get<Paged<ProjectDto>>('/api/projects?pageSize=200');
      if (!mounted.current) return;
      setProjects(result.items); setProjectError('');
      if (!projectRef.current && result.items.length) { projectRef.current = result.items[0].id; setProjectId(result.items[0].id); }
    } catch (failure) { if (mounted.current) setProjectError((failure as Error).message); }
  }, []);
  useEffect(() => { void loadProjects(); }, [loadProjects]);
  const loadList = useCallback(async () => {
    const currentProject = projectRef.current;
    if (!currentProject) return;
    const sequence = ++listSequence.current;
    setLoadingList(true);
    try {
      const result = await api.get<DocumentDto[]>('/api/documents?projectId=' + encodeURIComponent(currentProject));
      if (mounted.current && sequence === listSequence.current && currentProject === projectRef.current) { setDocuments(result); setListError(''); }
    } catch (failure) { if (mounted.current && sequence === listSequence.current) setListError((failure as Error).message); }
    finally { if (mounted.current && sequence === listSequence.current) setLoadingList(false); }
  }, []);
  const loadMembers = useCallback(async () => {
    const currentProject = projectRef.current;
    if (!currentProject) return;
    try {
      const result = await api.get<MemberDto[]>('/api/projects/' + currentProject + '/members');
      if (mounted.current && currentProject === projectRef.current) { setMembers(result.filter((member) => member.isActive)); setMemberError(''); }
    } catch (failure) { if (mounted.current && currentProject === projectRef.current) setMemberError((failure as Error).message); }
  }, []);
  useEffect(() => { setDocuments([]); setMembers([]); void loadList(); void loadMembers(); }, [projectId, loadList, loadMembers]);

  const applyDocument = (document: DocumentDto) => {
    const next = toDraft(document);
    baseRef.current = document; draftRef.current = next; baseline.current = signature(next); remoteRef.current = null;
    setBase(document); setDraft(next); setRemote(null); setError(''); setSavedMessage(''); contentErrorRef.current = ''; setEditorKey((key) => key + 1);
  };
  const update = useCallback((patch: Partial<Draft>) => {
    if (!draftRef.current) return;
    const next = { ...draftRef.current, ...patch };
    draftRef.current = next; setDraft(next); setSavedMessage('');
  }, []);
  const contentError = useCallback((message: string) => { contentErrorRef.current = message; setError(message); }, []);

  const save = async (): Promise<boolean> => {
    if (saveAttempt.current) return saveAttempt.current;
    const current = draftRef.current, original = baseRef.current;
    if (!current || !dirty()) return true;
    if (remoteRef.current) { setError('Compara la versión guardada y prepara el reintento antes de guardar.'); return false; }
    if (contentErrorRef.current) { setError(contentErrorRef.current); return false; }
    if (original?.canEdit === false) { setError('Este documento es de solo lectura.'); return false; }
    if (!current.title.trim()) { setError('Escribe un título antes de guardar o cambiar de documento.'); titleInput.current?.focus(); return false; }
    const canManageAccess = !original || original.canManageAccess === true;
    if (canManageAccess && current.visibility === 1 && !current.readerIds.filter((id) => id !== (original?.authorId || user?.id)).length) { setError('Selecciona al menos una persona con quien compartir el documento.'); return false; }
    const capturedSignature = signature(current);
    const attempt = (async () => {
      savingRef.current = true; setSaving(true); setError(''); setSavedMessage('');
      try {
        const audience = canManageAccess ? { visibility: current.visibility, readerIds: current.visibility === 1 ? current.readerIds : [] } : {};
        const payload = { title: current.title.trim(), content: JSON.stringify(current.content), contentFormat: 'tiptap', ...audience };
        const result = original ? await api.put<DocumentDto>('/api/documents/' + original.id, { ...payload, rowVersion: original.rowVersion })
          : await api.post<DocumentDto>('/api/documents', { ...payload, projectId: projectRef.current, parentId: current.parentId });
        if (!mounted.current) return true;
        const savedDraft = toDraft(result);
        baseRef.current = result; baseline.current = signature(savedDraft); setBase(result);
        if (draftRef.current && signature(draftRef.current) === capturedSignature) { draftRef.current = savedDraft; setDraft(savedDraft); }
        setSavedMessage('Guardado · ' + fmtDateTime(result.updatedAt)); void loadList();
        return true;
      } catch (failure) {
        if (!mounted.current) return false;
        const problem = failure as Error & { status?: number };
        setError(problem.message);
        if (problem.status === 409 && original) {
          try {
            const latest = await api.get<DocumentDto>('/api/documents/' + original.id);
            if (mounted.current) { remoteRef.current = latest; setRemote(latest); }
          } catch { if (mounted.current) setError(problem.message + ' No se pudo cargar la versión actual. Tu borrador se conserva.'); }
        }
        return false;
      } finally { savingRef.current = false; if (mounted.current) setSaving(false); }
    })();
    saveAttempt.current = attempt;
    void attempt.finally(() => { if (saveAttempt.current === attempt) saveAttempt.current = null; });
    return attempt;
  };
  flushRef.current = async () => { while (dirty() || savingRef.current) if (!(await save())) return false; return true; };
  useEffect(() => register({ hasPendingChanges: () => dirty() || savingRef.current, saveBeforeLeaving: () => flushRef.current() }), [register]);

  const open = async (document: DocumentDto) => {
    if (document.id === baseRef.current?.id && !loadingDocument) return;
    if (!(await flushRef.current())) return;
    const sequence = ++documentSequence.current, currentProject = projectRef.current;
    setLoadingDocument(true); setError('');
    try {
      const result = await api.get<DocumentDto>('/api/documents/' + document.id);
      if (mounted.current && sequence === documentSequence.current && currentProject === projectRef.current) applyDocument(result);
    } catch (failure) { if (mounted.current && sequence === documentSequence.current) setError('No se pudo abrir el documento: ' + (failure as Error).message); }
    finally { if (mounted.current && sequence === documentSequence.current) setLoadingDocument(false); }
  };
  const create = async (parentId: string | null = null) => {
    if (!projectRef.current || !(await flushRef.current())) return;
    documentSequence.current++; setLoadingDocument(false);
    const next: Draft = { title: '', content: EMPTY_DOCUMENT, parentId, visibility: 2, readerIds: [] };
    baseRef.current = null; draftRef.current = next; baseline.current = signature(next); remoteRef.current = null; contentErrorRef.current = '';
    setBase(null); setDraft(next); setRemote(null); setError(''); setSavedMessage(''); setEditorKey((key) => key + 1);
    requestAnimationFrame(() => titleInput.current?.focus());
  };
  const changeProject = async (nextProject: string) => {
    if (nextProject === projectRef.current || !(await flushRef.current())) return;
    projectRef.current = nextProject; documentSequence.current++; setLoadingDocument(false);
    baseRef.current = null; draftRef.current = null; remoteRef.current = null; baseline.current = '';
    setBase(null); setDraft(null); setRemote(null); setError(''); setSavedMessage(''); setProjectId(nextProject);
  };
  const discard = () => {
    if (!window.confirm('¿Descartar los cambios que todavía no has guardado?')) return;
    if (baseRef.current) applyDocument(baseRef.current);
    else { draftRef.current = null; baseline.current = ''; setDraft(null); setError(''); setSavedMessage(''); }
  };
  const canEdit = !base || base.canEdit !== false;
  const canManageAccess = !base || base.canManageAccess === true;

  return <div className="documents-page">
    <div className="row documents-header"><div><h1 className="page-title">Documentos</h1><p className="muted small">Notas, acuerdos y documentación del proyecto.</p></div><span className="grow" />
      <label className="documents-project-label">Proyecto<select aria-label="Proyecto de documentos" className="input" value={projectId} disabled={saving} onChange={(event) => void changeProject(event.target.value)}>{projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
      <button className="btn primary" disabled={!projectId || saving || loadingDocument} onClick={() => void create()}>+ Nuevo documento</button>
    </div>
    {projectError && <div className="alert error" role="alert">No se pudieron cargar los proyectos: {projectError} <button className="btn sm" onClick={() => void loadProjects()}>Reintentar proyectos</button></div>}
    {error && <div className="alert error" role="alert">{error}</div>}
    <div className="documents-layout">
      <aside className="card documents-tree" aria-label="Documentos del proyecto"><div className="row"><strong className="small">Páginas</strong><span className="grow" /><button className="btn sm" aria-label="Actualizar documentos" disabled={loadingList} onClick={() => void loadList()}>↻</button></div>
        {listError && <div className="alert error" role="alert">{listError}<button className="btn sm" onClick={() => void loadList()}>Reintentar lista</button></div>}
        {loadingList && <p className="muted small" role="status">Cargando documentos…</p>}
        {orderedDocuments(documents).map(({ document, depth }) => <button key={document.id} className={'documents-tree-item' + (base?.id === document.id ? ' active' : '')} style={{ paddingLeft: 10 + Math.min(depth, 6) * 14 }} aria-current={base?.id === document.id ? 'page' : undefined} onClick={() => void open(document)} disabled={saving}><span>▤</span><span>{document.title}</span><small>{document.visibility === 0 ? 'Proyecto' : document.visibility === 1 ? 'Compartido' : 'Privado'}</small></button>)}
        {!loadingList && !listError && documents.length === 0 && <div className="empty small">Todavía no hay documentos accesibles en este proyecto.</div>}
      </aside>
      <section className="card documents-main" aria-busy={loadingDocument || saving}>
        {loadingDocument && <div className="muted small" role="status">Abriendo documento…</div>}
        {draft ? <>
          <div className="documents-title-row"><input ref={titleInput} aria-label="Título del documento" className="input document-title-input" placeholder="Título del documento" value={draft.title} onChange={(event) => update({ title: event.target.value })} disabled={!canEdit || saving || loadingDocument} maxLength={250} />{base && <span className="muted small">{canEdit ? 'Documento' : 'Solo lectura'} · {fmtDateTime(base.updatedAt)}</span>}</div>
          {remote && <section className="document-conflict" aria-label="Conflicto de documento"><h2>Otra persona guardó cambios</h2><p>Tu borrador se conserva. Compara la versión guardada antes de reintentar.</p><strong>{remote.title}</strong><p className="muted small">Acceso actual: {VISIBILITY[remote.visibility ?? 2]}</p><div className="document-conflict-content" aria-label="Contenido guardado actualmente">{readableContent(remote)}</div>
            <button className="btn" onClick={() => {
              baseRef.current = remote; baseline.current = signature(toDraft(remote)); setBase(remote); remoteRef.current = null; setRemote(null); setError('');
            }}>Conservar mi borrador y preparar reintento</button></section>}
          <DocumentRichEditor key={editorKey} value={draft.content} editable={canEdit && !saving && !loadingDocument} onChange={(content) => update({ content })} onError={contentError} />
          <section className="document-sharing" aria-labelledby="document-sharing-title"><h2 id="document-sharing-title">Quién puede leer esta nota</h2>
            {canManageAccess ? <><div className="document-visibility-options">{VISIBILITY.map((label, index) => <label key={label}><input type="radio" name="document-visibility" value={index} checked={draft.visibility === index} disabled={saving || loadingDocument} onChange={() => update({ visibility: index as Draft['visibility'], readerIds: index === 1 ? draft.readerIds : [] })} />{label}</label>)}</div>
              {draft.visibility === 1 && <><p className="muted small">Las personas seleccionadas pueden leer la nota. Los permisos del proyecto determinan quién puede editarla.</p>{memberError && <div className="alert warn" role="alert">{memberError}<button className="btn sm" onClick={() => void loadMembers()}>Reintentar personas</button></div>}
                <div className="document-readers">{members.filter((member) => member.userId !== (base?.authorId || user?.id)).map((member) => <label key={member.userId}><input type="checkbox" checked={draft.readerIds.includes(member.userId)} disabled={saving || loadingDocument} onChange={(event) => update({ readerIds: event.target.checked ? [...draft.readerIds, member.userId] : draft.readerIds.filter((id) => id !== member.userId) })} /><span>{member.name}</span></label>)}</div></>}
            </> : <p className="small">{VISIBILITY[draft.visibility]}. El autor administra el acceso.</p>}
          </section>
          <div className="row documents-save-row" aria-live="polite"><span className="muted small">{saving ? 'Guardando…' : savedMessage || (dirty() ? 'Cambios sin guardar' : canEdit ? 'Sin cambios pendientes' : 'Solo lectura')}</span><span className="grow" />
            {base && canEdit && <button className="btn" disabled={saving || loadingDocument} onClick={() => void create(base.id)}>Nueva subpágina</button>}
            {dirty() && <button className="btn" disabled={saving || loadingDocument} onClick={discard}>Descartar cambios</button>}
            {canEdit && <button className="btn primary" disabled={saving || loadingDocument || !!remote || !dirty()} onClick={() => void save()}>Guardar documento</button>}
          </div>
        </> : !loadingDocument && <div className="empty">Selecciona un documento o crea una nota nueva.</div>}
      </section>
    </div>
  </div>;
}
