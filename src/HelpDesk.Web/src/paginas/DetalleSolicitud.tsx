import { useEffect, useMemo, useState } from 'react';
import { LuCheck, LuHistory, LuLightbulb, LuPencil, LuTrash2, LuX } from 'react-icons/lu';
import { api, ErrorApi } from '../api';
import { Cargando, Confirmar, Dialogo, ErrorCaja, Estado, PanelLateral, Prioridad } from '../componentes/basicos';
import { CamposSolicitud, CasosSimilares, entradaDesdeForm, ErroresForm, formDesdeSolicitud, FormSolicitud, validarForm } from '../componentes/FormularioSolicitud';
import { activos, mensajeError, useAvisar, useCatalogos, useUsuario } from '../estado/contextos';
import type { Conflicto, HistorialItem, SolicitudDetalle, SolicitudEntrada } from '../tipos';
import { duracion, fechaCorta, fechaHora, fechaHoraLarga, hora, isoLocal, aFecha, tiempoDesde } from '../util/formato';
import { emitir, useAhora, useCarga } from '../util/hooks';
import { cambiarParams } from '../util/rutas';

type Pestana = 'datos' | 'historial' | 'similares';

export function DetalleSolicitud({ id, onCerrar }: { id: number | null; onCerrar: () => void }) {
  const [confirmarCierre, setConfirmarCierre] = useState(false);
  const [modificado, setModificado] = useState(false);

  const intentarCerrar = () => {
    if (modificado) setConfirmarCierre(true);
    else onCerrar();
  };

  return (
    <PanelLateral abierto={id !== null} onCerrar={intentarCerrar} etiqueta={`Solicitud ${id ?? ''}`}>
      {id !== null && <Contenido key={id} id={id} onCerrar={intentarCerrar} onCerrarForzado={onCerrar} alModificar={setModificado} />}
      <Confirmar
        abierto={confirmarCierre}
        titulo="Hay cambios sin guardar"
        texto="Si cerrás ahora, se pierden los cambios que hiciste en esta solicitud."
        accion="Cerrar sin guardar"
        peligro
        onCancelar={() => setConfirmarCierre(false)}
        onConfirmar={() => {
          setConfirmarCierre(false);
          setModificado(false);
          onCerrar();
        }}
      />
    </PanelLateral>
  );
}

function Contenido({ id, onCerrar, onCerrarForzado, alModificar }: { id: number; onCerrar: () => void; onCerrarForzado: () => void; alModificar: (m: boolean) => void }) {
  const usuario = useUsuario();
  const catalogos = useCatalogos();
  const avisar = useAvisar();
  const ahora = useAhora(60_000);
  const { datos: detalle, error, recargar, setDatos } = useCarga(() => api.get<SolicitudDetalle>(`/api/solicitudes/${id}`), [id], 'solicitudes');

  const [pestana, setPestana] = useState<Pestana>('datos');
  const [form, setForm] = useState<FormSolicitud | null>(null);
  const [base, setBase] = useState<FormSolicitud | null>(null);
  const [errores, setErrores] = useState<ErroresForm>({});
  const [guardando, setGuardando] = useState(false);
  const [conflictos, setConflictos] = useState<{ lista: Conflicto[]; actual: SolicitudDetalle } | null>(null);
  const [errorGuardar, setErrorGuardar] = useState<string | null>(null);
  const [corrigiendoFecha, setCorrigiendoFecha] = useState(false);
  const [eliminando, setEliminando] = useState(false);

  const modificado = useMemo(() => !!form && !!base && JSON.stringify(entradaDesdeForm(form)) !== JSON.stringify(entradaDesdeForm(base)), [form, base]);
  useEffect(() => alModificar(modificado), [modificado, alModificar]);

  // Cuando llegan datos nuevos y el usuario no está editando, se actualiza el formulario.
  useEffect(() => {
    if (!detalle) return;
    if (!modificado) {
      const f = formDesdeSolicitud(detalle);
      setForm(f);
      setBase(f);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [detalle]);

  const cambiar = <K extends keyof FormSolicitud>(campo: K, valor: FormSolicitud[K]) => {
    setForm((f) => (f ? { ...f, [campo]: valor } : f));
    if (errores[campo]) setErrores((e) => ({ ...e, [campo]: undefined }));
  };

  const estadoResueltoId = activos(catalogos.estados).filter((e) => e.esResuelto).sort((a, b) => Number(b.predeterminado) - Number(a.predeterminado) || a.orden - b.orden)[0]?.id;

  const guardar = async (extra?: Partial<SolicitudEntrada>, baseConflicto?: SolicitudDetalle) => {
    if (!form || !base || !detalle) return;
    const encontrados = validarForm(form);
    // Solicitudes importadas pueden no tener medio/estado/prioridad: solo se exige lo que se modifica.
    const nueva = { ...entradaDesdeForm(form), ...extra };
    const original = entradaDesdeForm(base);
    const cambios: Partial<SolicitudEntrada> = {};
    (Object.keys(nueva) as (keyof SolicitudEntrada)[]).forEach((k) => {
      if (JSON.stringify(nueva[k]) !== JSON.stringify(original[k])) (cambios as Record<string, unknown>)[k] = nueva[k];
    });
    const erroresRelevantes: ErroresForm = {};
    (Object.keys(encontrados) as (keyof FormSolicitud)[]).forEach((k) => {
      if (k in cambios || k === 'descripcion' || k === 'duracionEstimadaMin') erroresRelevantes[k] = encontrados[k];
    });
    if (!base.descripcion.trim() && !('descripcion' in cambios)) delete erroresRelevantes.descripcion;
    setErrores(erroresRelevantes);
    if (Object.keys(erroresRelevantes).length > 0) {
      setPestana('datos');
      return;
    }
    if (Object.keys(cambios).length === 0) return;

    // Valor que el usuario tenía en pantalla antes de editar (para detectar ediciones simultáneas).
    const referencia = baseConflicto ? entradaDesdeForm(formDesdeSolicitud(baseConflicto)) : original;
    const valoresBase: Partial<SolicitudEntrada> = {};
    (Object.keys(cambios) as (keyof SolicitudEntrada)[]).forEach((k) => {
      (valoresBase as Record<string, unknown>)[k] = referencia[k];
    });

    setGuardando(true);
    setErrorGuardar(null);
    try {
      const actualizado = await api.patch<SolicitudDetalle>(`/api/solicitudes/${id}`, { cambios, base: valoresBase });
      const f = formDesdeSolicitud(actualizado);
      setForm(f);
      setBase(f);
      setConflictos(null);
      setDatos(actualizado);
      emitir('solicitudes');
      avisar(
        extra?.estadoId && actualizado.estadoEsResuelto
          ? `Solicitud #${id} resuelta${actualizado.minutosResolucion ? ` en ${duracion(actualizado.minutosResolucion)}` : ''}.`
          : `Cambios guardados en la solicitud #${id}.`,
      );
    } catch (e) {
      if (e instanceof ErrorApi && e.estado === 409 && e.datos?.conflictos) {
        setConflictos({ lista: e.datos.conflictos, actual: e.datos.actual });
      } else {
        setErrorGuardar(mensajeError(e));
      }
    } finally {
      setGuardando(false);
    }
  };

  const descartar = () => {
    if (!detalle) return;
    const fuente = conflictos?.actual ?? detalle;
    const f = formDesdeSolicitud(fuente);
    setForm(f);
    setBase(f);
    setConflictos(null);
    setErrores({});
    if (conflictos) setDatos(conflictos.actual);
  };

  if (error) {
    return (
      <div className="panel-cuerpo">
        <ErrorCaja error={error} reintentar={recargar} />
        <button type="button" className="btn" style={{ marginTop: 12 }} onClick={onCerrarForzado}>
          Cerrar
        </button>
      </div>
    );
  }
  if (!detalle || !form) return <Cargando />;

  const pendiente = detalle.estadoId !== null && detalle.estadoEsResuelto === false;

  return (
    <>
      <div className="panel-cabecera">
        <div className="detalle-titulo">
          <span className="sello">
            <span className="sello-etiqueta">Solicitud</span>
            <span className="sello-numero">#{detalle.id}</span>
          </span>
          <Estado nombre={detalle.estado} esResuelto={detalle.estadoEsResuelto} color={detalle.estadoColor} />
          <Prioridad nombre={detalle.prioridad} nivel={detalle.prioridadNivel} />
          <button type="button" className="btn btn-sutil btn-icono" style={{ marginLeft: 'auto' }} onClick={onCerrar} aria-label="Cerrar">
            <LuX />
          </button>
        </div>
        <div className="detalle-meta">
          Ingresó el {fechaHoraLarga(detalle.fechaIngreso).toLowerCase()}.{' '}
          {detalle.origen === 'IMPORTACION'
            ? `Importada del Excel${detalle.filaExcel ? ` (fila ${detalle.filaExcel})` : ''}.`
            : `Registrada por ${detalle.creadoPor ?? 'un usuario'}.`}
          {usuario.rol === 'ADMIN' && (
            <>
              {' '}
              <button type="button" className="btn btn-chico btn-sutil" onClick={() => setCorrigiendoFecha(true)}>
                <LuPencil /> Corregir fecha
              </button>
            </>
          )}
        </div>
        <div className="pestanas-panel" role="tablist">
          <button type="button" role="tab" aria-selected={pestana === 'datos'} className={`pestana-panel${pestana === 'datos' ? ' activa' : ''}`} onClick={() => setPestana('datos')}>
            Datos
          </button>
          <button type="button" role="tab" aria-selected={pestana === 'historial'} className={`pestana-panel${pestana === 'historial' ? ' activa' : ''}`} onClick={() => setPestana('historial')}>
            <LuHistory aria-hidden style={{ verticalAlign: -2 }} /> Historial ({detalle.historial.length})
          </button>
          <button type="button" role="tab" aria-selected={pestana === 'similares'} className={`pestana-panel${pestana === 'similares' ? ' activa' : ''}`} onClick={() => setPestana('similares')}>
            <LuLightbulb aria-hidden style={{ verticalAlign: -2 }} /> Casos parecidos
          </button>
        </div>
      </div>

      <div className="panel-cuerpo">
        {conflictos && (
          <div className="conflicto" role="alert">
            <strong>Otra persona modificó esta solicitud mientras la editabas.</strong>
            <ul>
              {conflictos.lista.map((c) => (
                <li key={c.campo}>
                  {c.etiqueta}: ahora dice “{c.valorActual ?? 'vacío'}” ({c.actualizadoPor ?? 'otro usuario'}, {hora(c.actualizadoEn)})
                </li>
              ))}
            </ul>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <button type="button" className="btn btn-chico" onClick={descartar}>
                Ver lo nuevo y descartar mis cambios
              </button>
              <button type="button" className="btn btn-chico btn-primario" onClick={() => void guardar(undefined, conflictos.actual)}>
                Guardar mis cambios igual
              </button>
            </div>
          </div>
        )}

        {pestana === 'datos' && (
          <>
            {pendiente && (
              <div className="aviso-pendiente">
                <span>
                  Pendiente hace <strong>{tiempoDesde(detalle.fechaIngreso, ahora)}</strong>
                </span>
                {estadoResueltoId && (
                  <button type="button" className="btn btn-chico btn-ok" style={{ marginLeft: 'auto' }} disabled={guardando} onClick={() => void guardar({ estadoId: estadoResueltoId })}>
                    <LuCheck /> {modificado ? 'Guardar y resolver' : 'Resolver'}
                  </button>
                )}
              </div>
            )}
            {detalle.fechaResolucion && (
              <div className="resolucion">
                <div>
                  Resuelta
                  <strong>{fechaHora(detalle.fechaResolucion)}</strong>
                </div>
                <div>
                  Tiempo total desde el ingreso
                  <strong>{detalle.minutosResolucion ? duracion(detalle.minutosResolucion) : 'En el momento'}</strong>
                </div>
              </div>
            )}
            <div className="detalle-grilla">
              <CamposSolicitud form={form} cambiar={cambiar} errores={errores} />
            </div>
            {errorGuardar && (
              <div className="error-caja" role="alert" style={{ marginTop: 12 }}>
                {errorGuardar}
              </div>
            )}
            <p className="tenue" style={{ fontSize: 'var(--t-12)', marginTop: 16 }}>
              Última modificación: {fechaHora(detalle.actualizadoEn)}
              {detalle.actualizadoPor ? ` por ${detalle.actualizadoPor}` : ''}.
            </p>
          </>
        )}

        {pestana === 'historial' && <Historial items={detalle.historial} />}

        {pestana === 'similares' && (
          <CasosSimilares texto={form.descripcion || detalle.descripcion || ''} excluirId={detalle.id} onAbrir={(otro) => cambiarParams({ ver: otro })} />
        )}
      </div>

      <div className="panel-pie">
        {usuario.rol === 'ADMIN' && (
          <button type="button" className="btn btn-sutil btn-peligro" onClick={() => setEliminando(true)}>
            <LuTrash2 /> Eliminar
          </button>
        )}
        <span style={{ marginLeft: 'auto' }} />
        {modificado && (
          <button type="button" className="btn btn-sutil" onClick={descartar} disabled={guardando}>
            Descartar cambios
          </button>
        )}
        <button type="button" className="btn btn-primario" disabled={!modificado || guardando} onClick={() => void guardar()}>
          {guardando ? 'Guardando…' : 'Guardar cambios'}
        </button>
      </div>

      <CorregirFecha
        abierto={corrigiendoFecha}
        detalle={detalle}
        onCerrar={() => setCorrigiendoFecha(false)}
        onGuardado={(d) => {
          setDatos(d);
          emitir('solicitudes');
          avisar(`Fecha de ingreso de la solicitud #${d.id} corregida.`);
        }}
      />
      <Confirmar
        abierto={eliminando}
        titulo={`¿Eliminar la solicitud #${detalle.id}?`}
        texto={
          <p>
            Deja de aparecer en la tabla y en las estadísticas. Queda registrada en el historial y el número <strong>{detalle.id}</strong> no se vuelve a usar.
          </p>
        }
        accion="Eliminar"
        peligro
        onCancelar={() => setEliminando(false)}
        onConfirmar={async () => {
          setEliminando(false);
          try {
            await api.delete(`/api/solicitudes/${detalle.id}`);
            emitir('solicitudes');
            avisar(`Solicitud #${detalle.id} eliminada.`);
            alModificar(false);
            onCerrarForzado();
          } catch (e) {
            avisar(mensajeError(e), 'error');
          }
        }}
      />
    </>
  );
}

const FEMENINOS = ['Prioridad', 'Oficina', 'Descripción', 'Duración estimada (min)', 'Fecha de ingreso'];
const CAMPOS_TEXTO = ['Funcionario', 'Descripción'];

/** Texto principal de cada movimiento: "Responsable cambiado a Mauro", "Estado cambiado a Resuelto"… */
function describir(h: HistorialItem): { titulo: string; antes: boolean; ahora: boolean; detalle: string | null } {
  const fem = h.campo !== null && FEMENINOS.includes(h.campo);
  switch (h.accion) {
    case 'CREADA':
      return { titulo: 'Solicitud creada', antes: false, ahora: false, detalle: h.detalle };
    case 'IMPORTADA':
      return { titulo: 'Importada desde el Excel', antes: false, ahora: false, detalle: h.detalle };
    case 'ESTADO':
    case 'RESUELTA':
      return { titulo: `Estado cambiado a ${h.valorNuevo ?? 'vacío'}`, antes: true, ahora: false, detalle: h.detalle };
    case 'REABIERTA':
      return { titulo: `Reabierta: estado cambiado a ${h.valorNuevo ?? 'vacío'}`, antes: true, ahora: false, detalle: h.detalle };
    case 'FECHA_CORREGIDA':
      return { titulo: 'Fecha de ingreso corregida', antes: true, ahora: true, detalle: h.detalle ? `Motivo: ${h.detalle}` : null };
    case 'ELIMINADA':
      return { titulo: 'Solicitud eliminada', antes: false, ahora: false, detalle: h.detalle };
    case 'MODIFICADA':
      if (h.campo === 'Observaciones') return { titulo: h.detalle ?? 'Observaciones modificadas', antes: h.valorAnterior !== null, ahora: true, detalle: null };
      if (h.campo && CAMPOS_TEXTO.includes(h.campo)) return { titulo: `${h.campo} ${fem ? 'modificada' : 'modificado'}`, antes: true, ahora: true, detalle: h.detalle };
      return {
        titulo: h.valorNuevo !== null ? `${h.campo} ${fem ? 'cambiada' : 'cambiado'} a ${h.valorNuevo}` : `${h.campo} ${fem ? 'borrada' : 'borrado'}`,
        antes: h.valorAnterior !== null,
        ahora: false,
        detalle: h.detalle,
      };
    default:
      return { titulo: h.accion, antes: h.valorAnterior !== null, ahora: h.valorNuevo !== null, detalle: h.detalle };
  }
}

function Historial({ items }: { items: HistorialItem[] }) {
  if (items.length === 0) return <p className="tenue">Sin movimientos registrados.</p>;
  return (
    <ol className="historial">
      {[...items].reverse().map((h) => {
        const d = describir(h);
        return (
          <li key={h.id}>
            <div className="historial-cuando">
              {fechaCorta(h.fechaHora)}
              <br />
              {hora(h.fechaHora)}
            </div>
            <div className="historial-que">
              <strong>{d.titulo}</strong>
              {h.usuario && <span className="tenue"> por {h.usuario}</span>}
              {(d.antes || d.ahora) && (
                <div className="historial-cambio">
                  {d.antes && (
                    <>
                      <span className="tenue">Antes</span>
                      <span className="antes">{h.valorAnterior ?? 'vacío'}</span>
                    </>
                  )}
                  {d.ahora && (
                    <>
                      <span className="tenue">Ahora</span>
                      <span className="despues">{h.valorNuevo ?? 'vacío'}</span>
                    </>
                  )}
                </div>
              )}
              {d.detalle && (
                <div className="tenue" style={{ marginTop: 3 }}>
                  {d.detalle}
                </div>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

function CorregirFecha({ abierto, detalle, onCerrar, onGuardado }: { abierto: boolean; detalle: SolicitudDetalle; onCerrar: () => void; onGuardado: (d: SolicitudDetalle) => void }) {
  const original = aFecha(detalle.fechaIngreso)!;
  const [valor, setValor] = useState(isoLocal(original).slice(0, 16));
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string | null>(null);

  const guardar = async () => {
    setError(null);
    if (!motivo.trim()) return setError('Indicá el motivo de la corrección.');
    try {
      const d = await api.put<SolicitudDetalle>(`/api/solicitudes/${detalle.id}/fecha-ingreso`, { fechaIngreso: `${valor}:00`, motivo });
      onGuardado(d);
      onCerrar();
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Dialogo
      abierto={abierto}
      onCerrar={onCerrar}
      titulo="Corregir fecha de ingreso"
      pie={
        <>
          <button type="button" className="btn btn-sutil" onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primario" onClick={() => void guardar()}>
            Guardar corrección
          </button>
        </>
      }
    >
      <div className="dialogo-cuerpo" style={{ display: 'grid', gap: 12 }}>
        <p className="tenue" style={{ margin: 0 }}>
          La fecha de ingreso no cambia al editar una solicitud. Usá esta opción solo para corregir un error (por ejemplo, una fecha mal cargada en el Excel). El cambio queda en el historial.
        </p>
        <div className="campo">
          <label htmlFor="fecha-corregida">Fecha y hora correctas</label>
          <input id="fecha-corregida" type="datetime-local" className="entrada" value={valor} onChange={(e) => setValor(e.target.value)} />
        </div>
        <div className="campo">
          <label htmlFor="motivo-fecha">Motivo</label>
          <input id="motivo-fecha" className="entrada" value={motivo} onChange={(e) => setMotivo(e.target.value)} placeholder="Ej.: fecha mal escrita en el registro original" />
        </div>
        {error && <div className="error-caja">{error}</div>}
      </div>
    </Dialogo>
  );
}
