import { DragEvent, useRef, useState } from 'react';
import { LuFileSpreadsheet, LuUpload } from 'react-icons/lu';
import { api } from '../../api';
import { ErrorCaja } from '../../componentes/basicos';
import { mensajeError, useAvisar } from '../../estado/contextos';
import type { ImportacionHistorica, Previsualizacion, ResultadoImportacion } from '../../tipos';
import { fechaHora, numero, plural } from '../../util/formato';
import { emitir, useCarga } from '../../util/hooks';
import { Enlace } from '../../util/rutas';

export function Importar() {
  const avisar = useAvisar();
  const entrada = useRef<HTMLInputElement>(null);
  const [encima, setEncima] = useState(false);
  const [analizando, setAnalizando] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [vista, setVista] = useState<Previsualizacion | null>(null);
  const [resultado, setResultado] = useState<ResultadoImportacion | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [verIncidencias, setVerIncidencias] = useState<'error' | 'advertencia' | null>(null);
  const historial = useCarga(() => api.get<ImportacionHistorica[]>('/api/importacion'), [resultado]);

  const analizar = async (archivo: File) => {
    setError(null);
    setVista(null);
    setResultado(null);
    if (!/\.xlsx$/i.test(archivo.name)) {
      setError('Elegí un archivo de Excel con extensión .xlsx.');
      return;
    }
    setAnalizando(true);
    try {
      setVista(await api.subir<Previsualizacion>(`/api/importacion/previsualizar?nombre=${encodeURIComponent(archivo.name)}`, archivo));
    } catch (e) {
      setError(mensajeError(e));
    } finally {
      setAnalizando(false);
    }
  };

  const confirmar = async () => {
    if (!vista) return;
    setConfirmando(true);
    setError(null);
    try {
      const r = await api.post<ResultadoImportacion>('/api/importacion/confirmar', { token: vista.token });
      setResultado(r);
      setVista(null);
      emitir('solicitudes');
      emitir('catalogos');
      emitir('tareas');
      avisar(`Importación terminada: ${plural(r.solicitudesNuevas, 'solicitud nueva', 'solicitudes nuevas')}.`);
    } catch (e) {
      setError(mensajeError(e));
    } finally {
      setConfirmando(false);
    }
  };

  const soltar = (e: DragEvent) => {
    e.preventDefault();
    setEncima(false);
    const archivo = e.dataTransfer.files?.[0];
    if (archivo) void analizar(archivo);
  };

  const errores = vista?.incidencias.filter((i) => i.tipo === 'error') ?? [];
  const advertencias = vista?.incidencias.filter((i) => i.tipo === 'advertencia') ?? [];
  const nuevos = vista?.catalogosNuevos;
  const hayCatalogosNuevos =
    nuevos &&
    (nuevos.responsables.length + nuevos.responsablesInactivos.length + nuevos.medios.length + nuevos.tipos.length + nuevos.estados.length + nuevos.prioridades.length + nuevos.areas.length > 0 ||
      nuevos.oficinas > 0);

  return (
    <div style={{ maxWidth: 1000 }}>
      <p className="tenue" style={{ marginTop: 0 }}>
        Cargá el Excel del registro de la mesa de ayuda. Se lee la tabla de solicitudes (encabezados “ID Solicitud”, “Fecha y hora de ingreso”…) y la hoja “Detalle tareas extra”.
        Antes de guardar vas a ver un resumen. Las solicitudes cuyo ID ya existe no se vuelven a cargar, así que podés importar el mismo archivo más de una vez.
      </p>

      {!vista && (
        <label
          className={`zona-archivo${encima ? ' encima' : ''}`}
          onDragOver={(e) => {
            e.preventDefault();
            setEncima(true);
          }}
          onDragLeave={() => setEncima(false)}
          onDrop={soltar}
        >
          {analizando ? <LuFileSpreadsheet className="girando" /> : <LuUpload />}
          <strong>{analizando ? 'Analizando el archivo…' : 'Arrastrá el Excel acá o hacé clic para elegirlo'}</strong>
          <span className="tenue">Archivo .xlsx</span>
          <input
            ref={entrada}
            type="file"
            accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            className="oculto"
            onChange={(e) => {
              const archivo = e.target.files?.[0];
              if (archivo) void analizar(archivo);
              e.target.value = '';
            }}
          />
        </label>
      )}

      {error && (
        <div style={{ marginTop: 12 }}>
          <ErrorCaja error={{ message: error }} />
        </div>
      )}

      {vista && (
        <div className="tarjeta" style={{ marginTop: 4 }}>
          <h2 style={{ fontSize: 'var(--t-20)' }}>Resumen de “{vista.nombreArchivo}”</h2>
          <p className="tenue" style={{ margin: '4px 0 0' }}>
            {vista.hojaSolicitudes ? `Solicitudes leídas de la hoja “${vista.hojaSolicitudes}”` : 'No se encontró la tabla de solicitudes'}
            {vista.solicitudes.idMinimo !== null && ` (IDs ${vista.solicitudes.idMinimo} a ${vista.solicitudes.idMaximo})`}.{' '}
            {vista.hojaTareas ? `Tareas extra leídas de la hoja “${vista.hojaTareas}”.` : ''}
          </p>

          <div className="resumen-importacion">
            <div>
              <strong>{numero(vista.solicitudes.filasLeidas)}</strong>
              <span>registros encontrados</span>
            </div>
            <div>
              <strong style={{ color: 'var(--ok)' }}>{numero(vista.solicitudes.nuevas)}</strong>
              <span>nuevos</span>
            </div>
            <div>
              <strong>{numero(vista.solicitudes.existentes)}</strong>
              <span>ya existentes (se omiten)</span>
            </div>
            <div>
              <strong style={{ color: vista.solicitudes.conError ? 'var(--alert)' : undefined }}>{numero(vista.solicitudes.conError)}</strong>
              <span>con error (no se importan)</span>
            </div>
            <div>
              <strong style={{ color: vista.solicitudes.conAdvertencia ? 'var(--wait)' : undefined }}>{numero(vista.solicitudes.conAdvertencia)}</strong>
              <span>con advertencias (se importan)</span>
            </div>
            <div>
              <strong>{numero(vista.tareasExtra.nuevas)}</strong>
              <span>tareas extra nuevas de {numero(vista.tareasExtra.leidas)}</span>
            </div>
          </div>

          {vista.solicitudes.pendientes > 0 && (
            <p style={{ marginTop: 0 }}>
              Entre las nuevas hay <strong>{numero(vista.solicitudes.pendientes)}</strong> que no están resueltas: van a aparecer en Pendientes.
            </p>
          )}

          {hayCatalogosNuevos && nuevos && (
            <div className="aviso-datos" style={{ alignItems: 'flex-start', flexDirection: 'column', gap: 4 }}>
              <strong>Se van a agregar a los catálogos:</strong>
              {nuevos.oficinas > 0 && (
                <span>
                  {plural(nuevos.oficinas, 'oficina')} ({nuevos.oficinasEjemplo.join(', ')}
                  {nuevos.oficinas > nuevos.oficinasEjemplo.length ? '…' : ''})
                </span>
              )}
              {nuevos.responsables.length > 0 && <span>Responsables: {nuevos.responsables.join(', ')}</span>}
              {nuevos.responsablesInactivos.length > 0 && <span>Ex integrantes (desactivados, para las tareas extra): {nuevos.responsablesInactivos.join(', ')}</span>}
              {nuevos.medios.length > 0 && <span>Medios de contacto: {nuevos.medios.join(', ')}</span>}
              {nuevos.tipos.length > 0 && <span>Tipos: {nuevos.tipos.join(', ')}</span>}
              {nuevos.estados.length > 0 && <span>Estados: {nuevos.estados.join(', ')}</span>}
              {nuevos.prioridades.length > 0 && <span>Prioridades: {nuevos.prioridades.join(', ')}</span>}
              {nuevos.areas.length > 0 && <span>Áreas: {nuevos.areas.join(', ')}</span>}
            </div>
          )}

          {(errores.length > 0 || advertencias.length > 0) && (
            <div style={{ marginBottom: 14 }}>
              <div className="pestanas" style={{ width: 'fit-content', marginBottom: 8 }}>
                <button type="button" className={`pestana${verIncidencias === 'error' ? ' activa' : ''}`} onClick={() => setVerIncidencias(verIncidencias === 'error' ? null : 'error')}>
                  Errores <span className="cuenta">{numero(errores.length)}</span>
                </button>
                <button type="button" className={`pestana${verIncidencias === 'advertencia' ? ' activa' : ''}`} onClick={() => setVerIncidencias(verIncidencias === 'advertencia' ? null : 'advertencia')}>
                  Advertencias <span className="cuenta">{numero(advertencias.length)}</span>
                </button>
              </div>
              {verIncidencias && (
                <div className="planilla-marco" style={{ maxHeight: 320 }}>
                  <table className="tabla-simple">
                    <thead>
                      <tr>
                        <th>Hoja</th>
                        <th className="col-num">Fila</th>
                        <th className="col-num">ID</th>
                        <th>Detalle</th>
                      </tr>
                    </thead>
                    <tbody>
                      {(verIncidencias === 'error' ? errores : advertencias).map((i, k) => (
                        <tr key={k}>
                          <td>{i.hoja}</td>
                          <td className="col-num">{i.fila}</td>
                          <td className="col-num">{i.id ?? '—'}</td>
                          <td>{i.mensaje}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              {vista.totalIncidencias > vista.incidencias.length && (
                <p className="tenue">Se muestran las primeras {numero(vista.incidencias.length)} de {numero(vista.totalIncidencias)}.</p>
              )}
            </div>
          )}

          <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
            <button type="button" className="btn btn-sutil" onClick={() => setVista(null)} disabled={confirmando}>
              Elegir otro archivo
            </button>
            <button
              type="button"
              className="btn btn-primario"
              onClick={() => void confirmar()}
              disabled={confirmando || (vista.solicitudes.nuevas === 0 && vista.tareasExtra.nuevas === 0)}
            >
              {confirmando
                ? 'Importando…'
                : vista.solicitudes.nuevas === 0 && vista.tareasExtra.nuevas === 0
                  ? 'No hay nada nuevo para importar'
                  : `Importar ${plural(vista.solicitudes.nuevas, 'solicitud', 'solicitudes')}${vista.tareasExtra.nuevas ? ` y ${plural(vista.tareasExtra.nuevas, 'tarea extra', 'tareas extra')}` : ''}`}
            </button>
          </div>
        </div>
      )}

      {resultado && (
        <div className="tarjeta" style={{ marginTop: 12, borderLeft: '4px solid var(--ok-dot)' }}>
          <h2 style={{ fontSize: 'var(--t-16)' }}>Importación terminada</h2>
          <p style={{ marginBottom: 0 }}>
            Se cargaron {plural(resultado.solicitudesNuevas, 'solicitud', 'solicitudes')} y {plural(resultado.tareasExtraNuevas, 'tarea extra', 'tareas extra')}.{' '}
            {resultado.solicitudesExistentes > 0 && `${plural(resultado.solicitudesExistentes, 'solicitud ya existía', 'solicitudes ya existían')} y no se tocaron. `}
            La próxima solicitud que se registre va a ser la <strong className="num">#{resultado.proximoId}</strong>. <Enlace a="/solicitudes">Ver solicitudes</Enlace>
          </p>
        </div>
      )}

      <h2 style={{ fontSize: 'var(--t-16)', margin: '28px 0 8px' }}>Importaciones anteriores</h2>
      {historial.datos && historial.datos.length === 0 && <p className="tenue">Todavía no se importó ningún archivo.</p>}
      {historial.datos && historial.datos.length > 0 && (
        <table className="tabla-simple tarjeta" style={{ padding: 0 }}>
          <thead>
            <tr>
              <th>Fecha</th>
              <th>Archivo</th>
              <th>Usuario</th>
              <th className="col-num">Leídas</th>
              <th className="col-num">Nuevas</th>
              <th className="col-num">Existentes</th>
              <th className="col-num">Con error</th>
              <th className="col-num">Tareas extra</th>
            </tr>
          </thead>
          <tbody>
            {historial.datos.map((h) => (
              <tr key={h.id}>
                <td>{fechaHora(h.fechaHora)}</td>
                <td>{h.nombreArchivo}</td>
                <td>{h.usuario}</td>
                <td className="col-num">{numero(h.filasLeidas)}</td>
                <td className="col-num">{numero(h.solicitudesNuevas)}</td>
                <td className="col-num">{numero(h.solicitudesExistentes)}</td>
                <td className="col-num">{numero(h.filasConError)}</td>
                <td className="col-num">{numero(h.tareasExtraNuevas)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
