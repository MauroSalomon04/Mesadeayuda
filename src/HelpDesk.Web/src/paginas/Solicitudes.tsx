import { useEffect, useMemo, useRef, useState } from 'react';
import { LuArrowDown, LuArrowUp, LuCheck, LuDownload, LuFilter, LuPlus, LuSearch, LuX } from 'react-icons/lu';
import { api, descargar, query } from '../api';
import { Autocompletar, ErrorCaja, Estado, MenuDesplegable, Paginador, Prioridad, Resaltado, SelectorMultiple, Vacio } from '../componentes/basicos';
import { useAccionesSolicitud } from '../estado/acciones';
import { activos, useCatalogos, useContadores, useNuevaSolicitud, useUsuario } from '../estado/contextos';
import type { ItemCatalogo, Pagina, SolicitudFila, SugerenciaTexto } from '../tipos';
import { duracion, fechaCorta, hora, numero, plural } from '../util/formato';
import { useAtajo, useCarga, useDebounce } from '../util/hooks';
import { cambiarParams, useUbicacion } from '../util/rutas';

const RAPIDOS = [
  { clave: 'todos', texto: 'Todos' },
  { clave: 'pendientes', texto: 'Pendientes' },
  { clave: 'resueltos', texto: 'Resueltos' },
  { clave: 'mias', texto: 'Mis solicitudes' },
  { clave: 'hoy', texto: 'Hoy' },
] as const;

const FILTROS_LISTA = ['estado', 'responsable', 'prioridad', 'tipo', 'medio'] as const;
type FiltroLista = (typeof FILTROS_LISTA)[number];

const TAMANO_PAGINA = 100;

/** Lee "1,4,sin" de la URL. */
function leerLista(valor: string | null): (number | 'sin')[] {
  if (!valor) return [];
  return valor
    .split(',')
    .map((v) => (v === 'sin' ? 'sin' : Number(v)))
    .filter((v): v is number | 'sin' => v === 'sin' || Number.isFinite(v));
}

export function Solicitudes() {
  const usuario = useUsuario();
  const catalogos = useCatalogos();
  const { contadores } = useContadores();
  const { abrir: abrirNueva } = useNuevaSolicitud();
  const { cambiar, resolver } = useAccionesSolicitud();
  const { params } = useUbicacion();

  const q = params.get('q') ?? '';
  const rapido = params.get('rapido') ?? 'todos';
  const orden = params.get('orden') ?? 'id';
  const dir = params.get('dir') ?? 'desc';
  const pagina = Number(params.get('pagina')) || 1;
  const desde = params.get('desde') ?? '';
  const hasta = params.get('hasta') ?? '';
  const ofi = params.get('ofi') ?? '';
  const seleccion = Number(params.get('ver')) || null;

  // Búsqueda: se escribe localmente y se pasa a la URL con un pequeño retraso.
  const [texto, setTexto] = useState(q);
  const textoDiferido = useDebounce(texto, 300);
  const buscador = useRef<HTMLInputElement>(null);
  useEffect(() => setTexto(q), [q]);
  useEffect(() => {
    if (textoDiferido.trim() !== q.trim()) cambiarParams({ q: textoDiferido.trim() || null, pagina: null }, true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [textoDiferido]);
  useAtajo('/', () => buscador.current?.focus());

  const filtrosLista = Object.fromEntries(FILTROS_LISTA.map((f) => [f, leerLista(params.get(f))])) as Record<FiltroLista, (number | 'sin')[]>;
  const cantidadFiltros = FILTROS_LISTA.filter((f) => filtrosLista[f].length > 0).length + (desde ? 1 : 0) + (hasta ? 1 : 0) + (ofi ? 1 : 0);
  const [verFiltros, setVerFiltros] = useState(cantidadFiltros > 0);

  const consulta = query({
    q,
    rapido: rapido === 'todos' ? null : rapido,
    estado: params.get('estado'),
    responsable: params.get('responsable'),
    prioridad: params.get('prioridad'),
    tipo: params.get('tipo'),
    medio: params.get('medio'),
    ofi,
    desde,
    hasta,
    orden,
    dir,
  });

  const { datos, error, cargando, recargar } = useCarga(
    () => api.get<Pagina<SolicitudFila>>(`/api/solicitudes${consulta}${consulta ? '&' : '?'}pagina=${pagina}&tamano=${TAMANO_PAGINA}`),
    [consulta, pagina],
    'solicitudes',
  );

  const palabras = useMemo(() => q.split(/\s+/).filter(Boolean), [q]);

  const ordenar = (clave: string) => {
    if (orden === clave) cambiarParams({ dir: dir === 'desc' ? 'asc' : 'desc', pagina: null });
    else cambiarParams({ orden: clave, dir: clave === 'id' || clave === 'fecha' ? 'desc' : 'asc', pagina: null });
  };

  const opcionesDe = (lista: ItemCatalogo[]) => lista.map((i) => ({ id: i.id, nombre: i.nombre }));
  const titulo = rapido === 'mias' ? 'Mis solicitudes' : 'Solicitudes';

  const encabezadoColumna = (clave: string, textoColumna: string, clase = '') => (
    <th className={`${clase}${orden === clave ? ' ordenada' : ''}`} aria-sort={orden === clave ? (dir === 'asc' ? 'ascending' : 'descending') : undefined}>
      <button type="button" onClick={() => ordenar(clave)}>
        {textoColumna}
        {orden === clave && (dir === 'asc' ? <LuArrowUp aria-hidden /> : <LuArrowDown aria-hidden />)}
      </button>
    </th>
  );

  const quitarFiltros = () =>
    cambiarParams({ estado: null, responsable: null, prioridad: null, tipo: null, medio: null, ofi: null, desde: null, hasta: null, pagina: null });

  return (
    <>
      <header className="encabezado">
        <div style={{ marginRight: 'auto' }}>
          <h1>{titulo}</h1>
          <div className="encabezado-sub">
            {datos ? plural(datos.total, 'solicitud', 'solicitudes') : ' '}
            {rapido === 'mias' && usuario.responsableId === null && ' que registraste vos (tu usuario no está vinculado a un responsable)'}
          </div>
        </div>
        <div className="buscador">
          <LuSearch aria-hidden />
          <input
            ref={buscador}
            type="search"
            value={texto}
            onChange={(e) => setTexto(e.target.value)}
            placeholder="Buscar por número, funcionario, oficina, descripción u observaciones"
            aria-label="Buscar solicitudes"
            onKeyDown={(e) => {
              if (e.key === 'Escape' && texto) {
                e.stopPropagation();
                setTexto('');
              }
            }}
          />
          {!texto && <span className="tecla">/</span>}
        </div>
        <button type="button" className="btn btn-primario" onClick={abrirNueva}>
          <LuPlus /> Nueva solicitud
        </button>
      </header>

      <div className="contenido">
        <div className="barra-filtros">
          <div className="pestanas" role="tablist" aria-label="Filtros rápidos">
            {RAPIDOS.map((r) => (
              <button
                key={r.clave}
                type="button"
                role="tab"
                aria-selected={rapido === r.clave}
                className={`pestana${rapido === r.clave ? ' activa' : ''}`}
                onClick={() => cambiarParams({ rapido: r.clave === 'todos' ? null : r.clave, pagina: null })}
              >
                {r.texto}
                {r.clave === 'pendientes' && contadores && contadores.pendientes > 0 && <span className="cuenta">{numero(contadores.pendientes)}</span>}
              </button>
            ))}
            {contadores && contadores.sinEstado > 0 && (
              <button
                type="button"
                role="tab"
                aria-selected={rapido === 'sinestado'}
                className={`pestana${rapido === 'sinestado' ? ' activa' : ''}`}
                onClick={() => cambiarParams({ rapido: 'sinestado', pagina: null })}
                title="Solicitudes importadas sin estado: conviene revisarlas"
              >
                Sin estado <span className="cuenta">{numero(contadores.sinEstado)}</span>
              </button>
            )}
          </div>
          <span className="separador-v" />
          <button type="button" className={`btn btn-chico${verFiltros ? ' btn-sutil' : ''}`} onClick={() => setVerFiltros((v) => !v)} aria-expanded={verFiltros}>
            <LuFilter /> Filtros{cantidadFiltros > 0 && ` (${cantidadFiltros})`}
          </button>
          {cantidadFiltros > 0 && (
            <button type="button" className="btn btn-chico btn-sutil" onClick={quitarFiltros}>
              <LuX /> Quitar filtros
            </button>
          )}
          <div style={{ marginLeft: 'auto' }}>
            <MenuDesplegable
              claseBoton="btn btn-chico"
              etiqueta="Exportar"
              boton={
                <>
                  <LuDownload /> Exportar
                </>
              }
              titulo={datos ? `Exportar ${plural(datos.total, 'solicitud', 'solicitudes')} (filtros actuales)` : 'Exportar'}
              opciones={[
                { clave: 'xlsx', texto: 'Excel (.xlsx)', alElegir: () => descargar(`/api/solicitudes/exportar${consulta}${consulta ? '&' : '?'}formato=xlsx`) },
                { clave: 'csv', texto: 'CSV (separado por ;)', alElegir: () => descargar(`/api/solicitudes/exportar${consulta}${consulta ? '&' : '?'}formato=csv`) },
              ]}
            />
          </div>
        </div>

        {verFiltros && (
          <div className="filtros-avanzados">
            <SelectorMultiple
              etiqueta="Estado"
              opciones={opcionesDe(catalogos.estados)}
              valores={filtrosLista.estado}
              incluirVacio="Sin estado"
              onChange={(v) => cambiarParams({ estado: v.join(',') || null, pagina: null })}
            />
            <SelectorMultiple
              etiqueta="Responsable"
              opciones={opcionesDe(catalogos.responsables)}
              valores={filtrosLista.responsable}
              incluirVacio="Sin responsable"
              onChange={(v) => cambiarParams({ responsable: v.join(',') || null, pagina: null })}
            />
            <SelectorMultiple
              etiqueta="Prioridad"
              opciones={opcionesDe(catalogos.prioridades)}
              valores={filtrosLista.prioridad}
              incluirVacio="Sin prioridad"
              onChange={(v) => cambiarParams({ prioridad: v.join(',') || null, pagina: null })}
            />
            <SelectorMultiple
              etiqueta="Tipo de solicitud"
              opciones={opcionesDe(catalogos.tipos)}
              valores={filtrosLista.tipo}
              incluirVacio="Sin tipo"
              onChange={(v) => cambiarParams({ tipo: v.join(',') || null, pagina: null })}
            />
            <SelectorMultiple
              etiqueta="Medio de contacto"
              opciones={opcionesDe(catalogos.medios)}
              valores={filtrosLista.medio}
              incluirVacio="Sin medio"
              onChange={(v) => cambiarParams({ medio: v.join(',') || null, pagina: null })}
            />
            <FiltroOficina valor={ofi} />
            <div className="campo">
              <label htmlFor="f-desde">Fecha desde</label>
              <input id="f-desde" type="date" className="entrada" value={desde} max={hasta || undefined} onChange={(e) => cambiarParams({ desde: e.target.value || null, pagina: null })} />
            </div>
            <div className="campo">
              <label htmlFor="f-hasta">Fecha hasta</label>
              <input id="f-hasta" type="date" className="entrada" value={hasta} min={desde || undefined} onChange={(e) => cambiarParams({ hasta: e.target.value || null, pagina: null })} />
            </div>
          </div>
        )}

        <ErrorCaja error={error} reintentar={recargar} />

        <div className="planilla-marco" aria-busy={cargando}>
          <table className="planilla">
            <thead>
              <tr>
                {encabezadoColumna('id', 'ID', 'col-id')}
                {encabezadoColumna('fecha', 'Fecha y hora')}
                {encabezadoColumna('funcionario', 'Funcionario')}
                {encabezadoColumna('oficina', 'Oficina')}
                {encabezadoColumna('medio', 'Medio')}
                {encabezadoColumna('tipo', 'Tipo')}
                {encabezadoColumna('descripcion', 'Descripción')}
                {encabezadoColumna('observaciones', 'Observaciones')}
                {encabezadoColumna('responsable', 'Responsable')}
                {encabezadoColumna('duracion', 'Duración', 'col-num')}
                {encabezadoColumna('estado', 'Estado')}
                {encabezadoColumna('prioridad', 'Prioridad')}
                <th aria-label="Acciones" />
              </tr>
            </thead>
            <tbody>
              {datos?.items.map((s) => {
                const pendiente = s.estadoId !== null && s.estadoEsResuelto === false;
                return (
                  <tr
                    key={s.id}
                    className={`${pendiente ? 'pendiente' : ''}${seleccion === s.id ? ' seleccionada' : ''}`}
                    onClick={() => cambiarParams({ ver: s.id })}
                  >
                    <td className="col-id">
                      <Resaltado texto={String(s.id)} palabras={palabras} />
                    </td>
                    <td className="col-fecha">
                      {fechaCorta(s.fechaIngreso)}
                      <span className="hora">{hora(s.fechaIngreso)}</span>
                    </td>
                    <td className="col-texto">
                      <Resaltado texto={s.nombreFuncionario} palabras={palabras} />
                    </td>
                    <td className="col-texto">
                      <Resaltado texto={s.oficina} palabras={palabras} />
                    </td>
                    <td>{s.medioContacto}</td>
                    <td className="codigo">{s.tipoSolicitud}</td>
                    <td className="col-texto">
                      <span className="recorte">
                        <Resaltado texto={s.descripcion} palabras={palabras} />
                      </span>
                    </td>
                    <td className="col-obs">
                      <span className="recorte" title={s.observaciones ?? undefined}>
                        <Resaltado texto={s.observaciones} palabras={palabras} />
                      </span>
                    </td>
                    <td>
                      <MenuDesplegable
                        etiqueta={`Cambiar responsable de la solicitud ${s.id}`}
                        boton={s.responsable ? <Resaltado texto={s.responsable} palabras={palabras} /> : <span className="tenue">Asignar</span>}
                        titulo="Cambiar responsable"
                        opciones={activos(catalogos.responsables).map((r) => ({
                          clave: r.id,
                          texto: r.nombre,
                          actual: r.id === s.responsableId,
                          alElegir: () => {
                            if (r.id !== s.responsableId)
                              void cambiar(s.id, { responsableId: r.id }, `Solicitud #${s.id} asignada a ${r.nombre}.`, s.responsableId !== null ? { responsableId: s.responsableId } : undefined);
                          },
                        }))}
                      />
                    </td>
                    <td className="col-num">{s.duracionEstimadaMin !== null ? duracion(s.duracionEstimadaMin) : ''}</td>
                    <td>
                      <MenuDesplegable
                        etiqueta={`Cambiar estado de la solicitud ${s.id}`}
                        boton={<Estado nombre={s.estado} esResuelto={s.estadoEsResuelto} color={s.estadoColor} />}
                        titulo="Cambiar estado"
                        opciones={activos(catalogos.estados).map((e) => ({
                          clave: e.id,
                          texto: <Estado nombre={e.nombre} esResuelto={e.esResuelto} color={e.color} />,
                          actual: e.id === s.estadoId,
                          alElegir: () => {
                            if (e.id !== s.estadoId)
                              void cambiar(s.id, { estadoId: e.id }, `Solicitud #${s.id}: ${e.nombre}.`, s.estadoId !== null ? { estadoId: s.estadoId } : undefined);
                          },
                        }))}
                      />
                    </td>
                    <td>
                      <Prioridad nombre={s.prioridad} nivel={s.prioridadNivel} />
                    </td>
                    <td className="col-acciones">
                      {pendiente && (
                        <button
                          type="button"
                          className="btn btn-chico btn-ok btn-icono"
                          title="Resolver"
                          aria-label={`Resolver la solicitud ${s.id}`}
                          onClick={(e) => {
                            e.stopPropagation();
                            void resolver(s);
                          }}
                        >
                          <LuCheck />
                        </button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {datos && datos.items.length === 0 && (
            <Vacio titulo={q ? `No hay solicitudes que coincidan con “${q}”` : 'No hay solicitudes con estos filtros'}>
              <p>{q ? 'Probá con menos palabras o revisá los filtros activos.' : 'Cambiá los filtros o registrá una nueva solicitud.'}</p>
            </Vacio>
          )}
        </div>

        <div className="pie-tabla">
          <span>Clic en una fila para ver el detalle. Las filas resaltadas están pendientes.</span>
          {datos && datos.total > 0 && (
            <Paginador pagina={pagina} tamano={TAMANO_PAGINA} total={datos.total} onCambiar={(p) => cambiarParams({ pagina: p > 1 ? p : null })} />
          )}
        </div>
      </div>
    </>
  );
}

function FiltroOficina({ valor }: { valor: string }) {
  const [texto, setTexto] = useState(valor);
  const diferido = useDebounce(texto, 400);
  useEffect(() => setTexto(valor), [valor]);
  useEffect(() => {
    if (diferido.trim() !== valor) cambiarParams({ ofi: diferido.trim() || null, pagina: null }, true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [diferido]);
  return (
    <div className="campo">
      <label htmlFor="f-oficina">Oficina</label>
      <Autocompletar
        id="f-oficina"
        valor={texto}
        onChange={setTexto}
        placeholder="Parte del nombre"
        buscar={(t) =>
          api
            .get<SugerenciaTexto[]>(`/api/sugerencias/oficinas${query({ q: t })}`)
            .then((l) => l.map((s) => ({ clave: s.texto, texto: s.texto, extra: numero(s.cantidad) })))
        }
      />
    </div>
  );
}
