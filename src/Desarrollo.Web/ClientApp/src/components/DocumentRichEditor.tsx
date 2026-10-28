import { useEffect } from 'react';
import { EditorContent, useEditor } from '@tiptap/react';
import type { JSONContent } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import TaskList from '@tiptap/extension-task-list';
import TaskItem from '@tiptap/extension-task-item';
import TextAlign from '@tiptap/extension-text-align';

export const EMPTY_DOCUMENT: JSONContent = { type: 'doc', content: [{ type: 'paragraph' }] };

/** Plain legacy content is text, never an HTML input to the editor. */
export function documentContent(content?: string | null, format?: string): JSONContent {
  if (format !== 'tiptap') return {
    type: 'doc', content: (content || '').split('\n').map((text) => ({ type: 'paragraph', ...(text ? { content: [{ type: 'text', text }] } : {}) })),
  };
  const value = JSON.parse(content || JSON.stringify(EMPTY_DOCUMENT));
  if (value?.type !== 'doc' || !Array.isArray(value.content)) throw new Error('El contenido guardado no tiene un formato de documento válido.');
  return value;
}

export default function DocumentRichEditor({ value, editable, onChange, onError }: {
  value: JSONContent; editable: boolean; onChange: (value: JSONContent) => void; onError: (message: string) => void;
}) {
  const editor = useEditor({
    extensions: [
      StarterKit.configure({
        heading: { levels: [1, 2, 3] }, underline: false,
        link: { autolink: false, linkOnPaste: false, openOnClick: false, HTMLAttributes: { target: null, rel: null, class: null },
          isAllowedUri: (uri) => /^(https?:\/\/|mailto:)/i.test(uri.trim()) },
      }),
      TaskList, TaskItem.configure({ nested: true }),
      TextAlign.configure({ types: ['heading', 'paragraph'] }),
    ],
    content: value, editable, shouldRerenderOnTransaction: true,
    editorProps: { attributes: { 'aria-label': 'Contenido del documento', role: 'textbox', 'aria-multiline': 'true', spellcheck: 'true' } },
    onUpdate: ({ editor: current }) => onChange(current.getJSON()),
    onContentError: ({ error }) => onError('No se pudo abrir el contenido: ' + error.message),
    enableContentCheck: true,
  });
  useEffect(() => { editor?.setEditable(editable, false); }, [editor, editable]);
  useEffect(() => {
    if (!editor || JSON.stringify(editor.getJSON()) === JSON.stringify(value)) return;
    try { editor.commands.setContent(value, { emitUpdate: false, errorOnInvalidContent: true }); }
    catch (error) { onError('No se pudo abrir el contenido: ' + (error as Error).message); }
  }, [editor, value, onError]);
  if (!editor) return <div className="muted small" role="status">Preparando editor…</div>;
  const action = (label: string, text: string, active: boolean, run: () => void) => <button key={label} type="button" className={'rich-tool' + (active ? ' active' : '')} aria-label={label} aria-pressed={active} title={label} disabled={!editable} onMouseDown={(event) => event.preventDefault()} onClick={run}>{text}</button>;
  return <div className={'document-rich-editor' + (!editable ? ' read-only' : '')}>
    <div className="document-rich-toolbar" role="toolbar" aria-label="Formato del documento">
      {action('Negrita', 'N', editor.isActive('bold'), () => editor.chain().focus().toggleBold().run())}
      {action('Cursiva', 'C', editor.isActive('italic'), () => editor.chain().focus().toggleItalic().run())}
      {action('Párrafo', 'Párrafo', editor.isActive('paragraph'), () => editor.chain().focus().setParagraph().run())}
      {([1, 2, 3] as const).map((level) => action('Encabezado ' + level, 'H' + level, editor.isActive('heading', { level }), () => editor.chain().focus().toggleHeading({ level }).run()))}
      {action('Lista con viñetas', '• Lista', editor.isActive('bulletList'), () => editor.chain().focus().toggleBulletList().run())}
      {action('Lista numerada', '1. Lista', editor.isActive('orderedList'), () => editor.chain().focus().toggleOrderedList().run())}
      {action('Lista de verificación', '☑ Lista', editor.isActive('taskList'), () => editor.chain().focus().toggleTaskList().run())}
      {(['left', 'center', 'right', 'justify'] as const).map((alignment, index) => action(['Alinear izquierda', 'Centrar texto', 'Alinear derecha', 'Justificar texto'][index], ['⇤', '≡', '⇥', '☰'][index], editor.isActive({ textAlign: alignment }), () => editor.chain().focus().setTextAlign(alignment).run()))}
      <button type="button" className="rich-tool" aria-label="Deshacer" disabled={!editable || !editor.can().undo()} onClick={() => editor.chain().focus().undo().run()}>↶</button>
      <button type="button" className="rich-tool" aria-label="Rehacer" disabled={!editable || !editor.can().redo()} onClick={() => editor.chain().focus().redo().run()}>↷</button>
    </div>
    <EditorContent editor={editor} />
  </div>;
}
