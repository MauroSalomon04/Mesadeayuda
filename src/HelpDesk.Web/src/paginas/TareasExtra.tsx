import { useEffect, useState } from 'react';
import { LuDownload, LuPlus, LuSearch, LuTrash2 } from 'react-icons/lu';
import { api, descargar, query } from '../api';
import { Confirmar, Dialogo, ErrorCaja, Vacio } from '../componentes/basicos';
import { activos, mensajeError, useAvisar, useCatalogos } from '../estado/contextos';
import type { TareaExtra } from '../tipos';
import { fechaCorta, plural } from '../util/formato';
import { emitir, useCarga, useDebounce } from '../util/hooks';

export function TareasExtra() {
  const catalogos = useCatalogos();
  const [texto, setTexto] = useState('');
  const [area, setArea] = useState<number | ''>('');
  const diferido = useDebounce(texto, 300);
  const [editando, setEditando] = useState<TareaExtra | 'nueva' | null>(null);

  const consulta = query({ q: diferido.trim(), area: area === '' ? null : area });
  const { datos, error, recargar } = useCarga(() => api.get<TareaExtra[]>(`/api/tareas-extra${consulta}`), [consulta], 'tareas');

  return (
    <>
      <header className="encabezado">
        <div style={{ marginRight: 'auto' }}>
          <h1>Tareas extra</h1>
          <div className="encabezado-sub">Colaboraciones de la mesa de ayuda con otras áreas de ASSE, aparte de las solicitudes.</div>
        </div>
        <button type="button" className="btn" onClick={() => descargar(`/api/tareas-extra/exportar${consulta}`)}>
          <LuDownload /> Exportar a Excel
        </button>
        <button type="button" className="btn btn-primario" onClick={() => setEditando('nueva')}>
          <LuPlus /> Nueva tarea
        </button>
      </header>
      <div className="contenido">
        <div className="barra-filtros">
          <div className="buscador" style={{ maxWidth: 420 }}>
            <LuSearch aria-hidden />
            <input type="search" value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Buscar en tareas, impacto o carga" aria-label="Buscar tareas extra" style={{ height: 38 }} />
          </div>
          <select className="selector" style={{ width: 240 }} value={area} onChange={(e) => setArea(e.target.value === '' ? '' : Number(e.target.value))} aria-label="Área">
            <option value="">Todas las áreas</option>
            {catalogos.areas.map((a) => (
              <option key={a.id} value={a.id}>
                {a.nombre}
              </option>
            ))}
          </select>
          {datos && <span className="tenue">{plural(datos.length, 'tarea')}</span>}
        </div>

        <ErrorCaja error={error} reintentar={recargar} />

        <div className="planilla-marco">
          <table className="planilla">
            <thead>
              <tr>
                <th>
                  <span>Área de ASSE con quién se colabora</span>
                </th>
                <th>
                  <span>Tarea</span>
                </th>
                <th>
                  <span>Impacto</span>
                </th>
                <th>
                  <span>Carga de trabajo</span>
                </th>
                <th>
                  <span>Implicados de mesa de ayuda</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {datos?.map((t) => (
                <tr key={t.id} onClick={() => setEditando(t)}>
                  <td style={{ whiteSpace: 'nowrap' }}>{t.area ?? <span className="tenue">Sin área</span>}</td>
                  <td className="col-texto" style={{ fontWeight: 600 }}>
                    {t.tarea}
                  </td>
                  <td className="col-obs" style={{ maxWidth: 420 }}>
                    {t.impacto}
                  </td>
                  <td className="col-texto">{t.cargaTrabajo}</td>
                  <td>{t.implicados.map((i) => i.nombre).join(', ')}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {datos && datos.length === 0 && (
            <Vacio titulo="No hay tareas extra registradas">
              <p>Registrá acá los trabajos con otras áreas (Soporte, Arquitectura, Infraestructura…). También se importan desde la hoja “Detalle tareas extra” del Excel.</p>
            </Vacio>
          )}
        </div>
      </div>

      {editando && <EditorTarea tarea={editando === 'nueva' ? null : editando} areas={activos(catalogos.areas)} onCerrar={() => setEditando(null)} />}
    </>
  );
}

function EditorTarea({ tarea, areas, onCerrar }: { tarea: TareaExtra | null; areas: { id: number; nombre: string }[]; onCerrar: () => void }) {
  const catalogos = useCatalogos();
  const avisar = useAvisar();
  const [areaId, setAreaId] = useState<number | ''>(tarea?.areaAsseId ?? '');
  const [descripcion, setDescripcion] = useState(tarea?.tarea ?? '');
  const [impacto, setImpacto] = useState(tarea?.impacto ?? '');
  const [carga, setCarga] = useState(tarea?.cargaTrabajo ?? '');
  const [implicados, setImplicados] = useState<number[]>(tarea?.implicados.map((i) => i.id) ?? []);
  const [error, setError] = useState<string | null>(null);
  const [guardando, setGuardando] = useState(false);
  const [eliminando, setEliminando] = useState(false);

  useEffect(() => setError(null), [descripcion]);

  // Responsables activos + los ya implicados aunque estén desactivados (ex integrantes).
  const personas = catalogos.responsables.filter((r) => r.activo || implicados.includes(r.id));
  const opcionesArea = tarea?.areaAsseId && !areas.some((a) => a.id === tarea.areaAsseId) ? [...areas, { id: tarea.areaAsseId, nombre: tarea.area ?? '' }] : areas;

  const guardar = async () => {
    if (!descripcion.trim()) return setError('Describí la tarea.');
    setGuardando(true);
    try {
      const cuerpo = { areaAsseId: areaId === '' ? null : areaId, tarea: descripcion, impacto, cargaTrabajo: carga, implicadosIds: implicados };
      if (tarea) await api.put(`/api/tareas-extra/${tarea.id}`, cuerpo);
      else await api.post('/api/tareas-extra', cuerpo);
      emitir('tareas');
      avisar(tarea ? 'Tarea actualizada.' : 'Tarea registrada.');
      onCerrar();
    } catch (e) {
      setError(mensajeError(e));
    } finally {
      setGuardando(false);
    }
  };

  return (
    <Dialogo
      abierto
      onCerrar={onCerrar}
      titulo={tarea ? 'Tarea extra' : 'Nueva tarea extra'}
      clase=""
      pie={
        <>
          {tarea && (
            <button type="button" className="btn btn-sutil btn-peligro" style={{ marginRight: 'auto' }} onClick={() => setEliminando(true)}>
              <LuTrash2 /> Eliminar
            </button>
          )}
          <button type="button" className="btn btn-sutil" onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primario" disabled={guardando} onClick={() => void guardar()}>
            {tarea ? 'Guardar cambios' : 'Registrar tarea'}
          </button>
        </>
      }
    >
      <div className="dialogo-cuerpo" style={{ display: 'grid', gap: 12 }}>
        <div className="campo">
          <label htmlFor="te-area">Área de ASSE con quién se colabora</label>
          <select id="te-area" className="selector" value={areaId} onChange={(e) => setAreaId(e.target.value === '' ? '' : Number(e.target.value))}>
            <option value="">Sin área</option>
            {opcionesArea.map((a) => (
              <option key={a.id} value={a.id}>
                {a.nombre}
              </option>
            ))}
          </select>
        </div>
        <div className="campo">
          <label htmlFor="te-tarea">Tarea</label>
          <textarea id="te-tarea" className="area-texto" style={{ minHeight: 60 }} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} maxLength={500} autoFocus />
        </div>
        <div className="campo">
          <label htmlFor="te-impacto">Impacto</label>
          <textarea id="te-impacto" className="area-texto" value={impacto} onChange={(e) => setImpacto(e.target.value)} placeholder="Alto, medio o bajo, y por qué" />
        </div>
        <div className="campo">
          <label htmlFor="te-carga">Carga de trabajo</label>
          <input id="te-carga" className="entrada" value={carga} onChange={(e) => setCarga(e.target.value)} maxLength={1000} placeholder="Ej.: aprox. 30 hs de trabajo" />
        </div>
        <div className="campo">
          <span className="campo-etiqueta">Implicados de mesa de ayuda</span>
          <div className="seg">
            {personas.map((p) => {
              const marcado = implicados.includes(p.id);
              return (
                <label key={p.id} className={`seg-opcion${marcado ? ' marcada' : ''}`}>
                  <input type="checkbox" checked={marcado} onChange={() => setImplicados((l) => (marcado ? l.filter((x) => x !== p.id) : [...l, p.id]))} />
                  {p.nombre}
                  {!p.activo && <span className="tenue"> (ex integrante)</span>}
                </label>
              );
            })}
          </div>
        </div>
        {tarea && (
          <p className="tenue" style={{ fontSize: 'var(--t-12)', margin: 0 }}>
            {tarea.origen === 'IMPORTACION' ? 'Importada del Excel' : `Registrada por ${tarea.creadoPor ?? 'un usuario'}`} el {fechaCorta(tarea.creadoEn)}.
          </p>
        )}
        {error && <div className="error-caja">{error}</div>}
      </div>
      <Confirmar
        abierto={eliminando}
        titulo="¿Eliminar esta tarea extra?"
        texto="Deja de aparecer en la lista. La acción queda registrada en la auditoría."
        accion="Eliminar"
        peligro
        onCancelar={() => setEliminando(false)}
        onConfirmar={async () => {
          setEliminando(false);
          try {
            await api.delete(`/api/tareas-extra/${tarea!.id}`);
            emitir('tareas');
            avisar('Tarea eliminada.');
            onCerrar();
          } catch (e) {
            setError(mensajeError(e));
          }
        }}
      />
    </Dialogo>
  );
}
