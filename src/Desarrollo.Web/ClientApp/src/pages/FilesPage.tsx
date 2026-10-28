import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { api, qs } from '../api';
import type { Paged, ProjectDto, StoredFileDto } from '../types';
import { fmtDateTime } from '../constants';
import './folders.css';

const MAX_FILE_SIZE = 50 * 1024 * 1024;
const EXTENSIONS = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'pdf', 'txt', 'md', 'csv', 'zip', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx'];
const ACCEPT = EXTENSIONS.map((extension) => '.' + extension).join(',');
type FolderVisibility = 'Public' | 'Private';
function fileSize(size: number) { return size >= 1024 * 1024 ? (size / 1024 / 1024).toFixed(1) + ' MB' : (size / 1024).toFixed(1) + ' KB'; }
function previewable(file: StoredFileDto) { return ['image/png', 'image/jpeg', 'image/gif', 'image/webp'].includes(file.contentType.toLowerCase()); }

export default function FilesPage() {
  const [visibility, setVisibility] = useState<FolderVisibility>('Public');
  const [projects, setProjects] = useState<ProjectDto[]>([]);
  const [projectId, setProjectId] = useState('');
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [page, setPage] = useState(1);
  const [result, setResult] = useState<Paged<StoredFileDto>>({ items: [], total: 0, page: 1, pageSize: 50 });
  const [loading, setLoading] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [deleting, setDeleting] = useState('');
  const [selected, setSelected] = useState<File | null>(null);
  const [description, setDescription] = useState('');
  const [dragging, setDragging] = useState(false);
  const [error, setError] = useState('');
  const [uploadError, setUploadError] = useState('');
  const [projectError, setProjectError] = useState('');
  const [message, setMessage] = useState('');
  const [preview, setPreview] = useState<StoredFileDto | null>(null);
  const [previewError, setPreviewError] = useState(false);
  const mounted = useRef(true), sequence = useRef(0), uploadingRef = useRef(false);
  const fileInput = useRef<HTMLInputElement>(null);
  const uploadController = useRef<AbortController | null>(null);
  const previewClose = useRef<HTMLButtonElement>(null);

  useEffect(() => { mounted.current = true; return () => { mounted.current = false; sequence.current++; uploadController.current?.abort(); }; }, []);
  const loadProjects = useCallback(async () => {
    try {
      const response = await api.get<Paged<ProjectDto>>('/api/projects?pageSize=200');
      if (mounted.current) { setProjects(response.items); setProjectId((current) => current || response.items[0]?.id || ''); setProjectError(''); }
    } catch (failure) { if (mounted.current) setProjectError((failure as Error).message); }
  }, []);
  useEffect(() => { void loadProjects(); }, [loadProjects]);
  useEffect(() => { const timer = setTimeout(() => { setQuery(search.trim()); setPage(1); }, 250); return () => clearTimeout(timer); }, [search]);
  const load = useCallback(async () => {
    const request = ++sequence.current;
    if (visibility === 'Public' && !projectId) { setResult({ items: [], total: 0, page: 1, pageSize: 50 }); setLoading(false); return; }
    setLoading(true); setError('');
    try {
      const response = await api.get<Paged<StoredFileDto>>('/api/files' + qs({ visibility, projectId: visibility === 'Public' ? projectId : undefined, search: query, page, pageSize: 50 }));
      if (mounted.current && request === sequence.current) setResult(response);
    } catch (failure) { if (mounted.current && request === sequence.current) { setResult({ items: [], total: 0, page, pageSize: 50 }); setError((failure as Error).message); } }
    finally { if (mounted.current && request === sequence.current) setLoading(false); }
  }, [visibility, projectId, query, page]);
  useEffect(() => { setResult({ items: [], total: 0, page, pageSize: 50 }); void load(); }, [load, page]);
  useEffect(() => {
    if (!preview) return;
    const previous = document.activeElement as HTMLElement | null;
    previewClose.current?.focus();
    const key = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setPreview(null);
      if (event.key !== 'Tab') return;
      const controls = Array.from(document.querySelectorAll<HTMLElement>('.folder-preview button, .folder-preview a[href]'));
      const first = controls[0], last = controls[controls.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    };
    document.addEventListener('keydown', key);
    return () => { document.removeEventListener('keydown', key); if (previous?.isConnected) previous.focus(); };
  }, [preview]);
  const choose = (file?: File) => {
    setUploadError(''); setMessage('');
    if (!file) return;
    setSelected(null);
    const extension = file.name.split('.').pop()?.toLowerCase() || '';
    if (!EXTENSIONS.includes(extension)) { setUploadError('Ese tipo de archivo no está permitido. Selecciona una imagen PNG/JPG/GIF/WebP, documento o ZIP.'); return; }
    if (file.size > MAX_FILE_SIZE) { setUploadError('El archivo supera el límite de 50 MB.'); return; }
    if (file.size === 0) { setUploadError('El archivo está vacío.'); return; }
    setSelected(file);
  };
  const upload = async (event: FormEvent) => {
    event.preventDefault();
    if (uploadingRef.current) return;
    if (!selected) { setUploadError('Selecciona un archivo antes de subirlo.'); return; }
    if (visibility === 'Public' && !projectId) { setUploadError('Selecciona el proyecto de la carpeta pública.'); return; }
    uploadingRef.current = true; setUploading(true); setUploadError(''); setMessage('');
    const controller = new AbortController(); uploadController.current = controller;
    const data = new FormData(); data.append('file', selected); data.append('visibility', visibility);
    if (visibility === 'Public') data.append('projectId', projectId);
    if (description.trim()) data.append('description', description.trim());
    try {
      const response = await api.upload<StoredFileDto>('/api/files', data, controller.signal);
      if (!mounted.current) return;
      setSelected(null); setDescription(''); if (fileInput.current) fileInput.current.value = '';
      setMessage('Archivo subido · ' + response.code);
      if (page !== 1) setPage(1); else await load();
    } catch (failure) {
      if (mounted.current) setUploadError(controller.signal.aborted ? 'Carga cancelada. Actualiza la carpeta para comprobar si había terminado.' : (failure as Error).message);
    } finally { uploadingRef.current = false; if (mounted.current) setUploading(false); }
  };
  const remove = async (file: StoredFileDto) => {
    if (!file.canDelete || deleting || !window.confirm('¿Eliminar «' + file.name + '» de esta carpeta?')) return;
    setDeleting(file.id); setError(''); setMessage('');
    try { await api.del('/api/files/' + file.id); if (mounted.current) { setMessage('Archivo eliminado.'); if (result.items.length === 1 && page > 1) setPage(page - 1); else await load(); } }
    catch (failure) { if (mounted.current) setError('No se pudo eliminar el archivo: ' + (failure as Error).message); }
    finally { if (mounted.current) setDeleting(''); }
  };
  const copy = async (code: string) => {
    try { await navigator.clipboard.writeText(code); setMessage('Código copiado: ' + code); }
    catch { setError('No se pudo copiar el código. Puedes seleccionarlo y copiarlo manualmente.'); }
  };
  const pages = Math.max(1, Math.ceil(result.total / result.pageSize));
  return <div className="folders-page">
    <div className="row folders-header"><div><h1 className="page-title">Carpetas</h1><p className="muted small">Archivos compartidos por proyecto y tus archivos privados.</p></div><span className="grow" /><button className="btn" disabled={loading || uploading} onClick={() => void load()}>Actualizar</button></div>
    <div className="folder-tabs" role="tablist" aria-label="Visibilidad de la carpeta">{(['Public', 'Private'] as const).map((value) => <button key={value} role="tab" aria-selected={visibility === value} aria-controls="folder-content" className={'folder-tab' + (visibility === value ? ' active' : '')} disabled={uploading} onClick={() => { setVisibility(value); setPage(1); setError(''); setMessage(''); }}>{value === 'Public' ? 'Pública' : 'Privada'}</button>)}</div>
    <div className="folder-controls"><div><strong>{visibility === 'Public' ? 'Carpeta pública del proyecto' : 'Tu carpeta privada'}</strong><p className="muted small">{visibility === 'Public' ? 'Visible para las personas autorizadas del proyecto.' : 'Solo tú puedes consultar estos archivos.'}</p></div><span className="grow" />
      {visibility === 'Public' && <label>Proyecto<select className="input" aria-label="Proyecto de la carpeta pública" value={projectId} disabled={uploading} onChange={(event) => { setProjectId(event.target.value); setPage(1); }}><option value="">Selecciona un proyecto</option>{projects.map((project) => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>}
      <label>Buscar<input className="input" aria-label="Buscar archivos" placeholder="Nombre, código o descripción" value={search} disabled={uploading} onChange={(event) => setSearch(event.target.value)} /></label>
    </div>
    {projectError && <div className="alert error" role="alert">No se pudieron cargar los proyectos: {projectError}<button className="btn sm" onClick={() => void loadProjects()}>Reintentar proyectos</button></div>}
    <form className={'card folder-upload' + (dragging ? ' is-dragging' : '')} onSubmit={upload} onDragOver={(event) => { event.preventDefault(); if (!uploading) setDragging(true); }} onDragLeave={(event) => { if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setDragging(false); }} onDrop={(event) => { event.preventDefault(); setDragging(false); if (!uploading) { if (event.dataTransfer.files.length > 1) { setSelected(null); setUploadError('Sube un archivo a la vez.'); } else choose(event.dataTransfer.files[0]); } }}>
      <div><strong>Subir archivo</strong><p className="muted small">Arrastra un archivo o selecciónalo. Máximo 50 MB.</p><input ref={fileInput} type="file" aria-label="Seleccionar archivo" accept={ACCEPT} disabled={uploading} onChange={(event) => choose(event.target.files?.[0])} /></div>
      {selected && <div className="folder-selected"><strong>{selected.name}</strong><span className="muted small">{fileSize(selected.size)}</span><button type="button" className="btn sm" disabled={uploading} onClick={() => { setSelected(null); if (fileInput.current) fileInput.current.value = ''; }}>Quitar selección</button></div>}
      <label>Descripción (opcional)<input className="input" aria-label="Descripción del archivo" value={description} disabled={uploading} maxLength={2000} onChange={(event) => setDescription(event.target.value)} /></label>
      <div className="row"><span className="muted small">Imágenes, PDF, TXT, Markdown, CSV, ZIP y documentos de Office.</span><span className="grow" />{uploading && <button type="button" className="btn" onClick={() => uploadController.current?.abort()}>Cancelar carga</button>}<button className="btn primary" disabled={uploading || !selected || (visibility === 'Public' && !projectId)}>{uploading ? 'Subiendo archivo…' : 'Subir a carpeta ' + (visibility === 'Public' ? 'pública' : 'privada')}</button></div>
      {uploadError && <div className="alert error" role="alert">{uploadError}</div>}
    </form>
    {message && <div className="alert success" role="status">{message}</div>}
    {error && <div className="alert error" role="alert">{error}<button className="btn sm" onClick={() => void load()}>Reintentar carpeta</button></div>}
    <section id="folder-content" role="tabpanel" aria-label={visibility === 'Public' ? 'Archivos públicos del proyecto' : 'Archivos privados'} aria-busy={loading}>
      {loading && <div className="empty" role="status">Cargando archivos…</div>}
      {!loading && visibility === 'Public' && !projectId ? <div className="empty">Selecciona un proyecto para abrir su carpeta pública.</div> : !loading && !error && result.items.length === 0 ? <div className="empty">{query ? 'No hay archivos que coincidan con la búsqueda.' : 'Esta carpeta todavía no tiene archivos.'}</div> : null}
      <div className="folder-file-grid">{result.items.map((file) => <article key={file.id} className="card folder-file-card">
        {previewable(file) ? <button className="folder-image-button" aria-label={'Ver imagen ' + file.name} onClick={() => { setPreview(file); setPreviewError(false); }}><img src={'/api/files/' + file.id + '/preview'} alt={file.name} loading="lazy" /></button> : <div className="folder-file-icon" aria-hidden="true">▤</div>}
        <h2>{file.name}</h2><p className="folder-file-code"><code>{file.code}</code><button className="btn sm" aria-label={'Copiar código ' + file.code} onClick={() => void copy(file.code)}>Copiar</button></p>
        <div className="muted small">{fileSize(file.sizeBytes)} · {fmtDateTime(file.createdAt)}</div>{file.description && <p className="folder-file-description small">{file.description}</p>}
        <div className="folder-file-actions"><a className="btn sm" href={'/api/files/' + file.id + '/download'} download={file.name}>Descargar</a>{file.canDelete && <button className="btn sm danger" disabled={!!deleting} onClick={() => void remove(file)}>{deleting === file.id ? 'Eliminando…' : 'Eliminar'}</button>}</div>
      </article>)}</div>
      {result.total > 0 && <div className="folder-pagination"><span className="muted small">{result.total} archivos · Página {page} de {pages}</span><span className="grow" /><button className="btn" disabled={loading || uploading || page <= 1} onClick={() => setPage(page - 1)}>Anterior</button><button className="btn" disabled={loading || uploading || page >= pages} onClick={() => setPage(page + 1)}>Siguiente</button></div>}
    </section>
    {preview && <div className="modal-backdrop" onClick={() => setPreview(null)}><div className="modal card folder-preview" role="dialog" aria-modal="true" aria-labelledby="folder-preview-title" onClick={(event) => event.stopPropagation()}><div className="row"><h2 id="folder-preview-title">{preview.name}</h2><span className="grow" /><button ref={previewClose} className="btn" aria-label="Cerrar imagen" onClick={() => setPreview(null)}>✕</button></div>{previewError ? <div className="alert error" role="alert">No se pudo cargar la imagen. Comprueba tu acceso o descarga el archivo.</div> : <img src={'/api/files/' + preview.id + '/preview'} alt={preview.name} onError={() => setPreviewError(true)} />}<a className="btn" href={'/api/files/' + preview.id + '/download'} download={preview.name}>Descargar original</a></div></div>}
  </div>;
}
