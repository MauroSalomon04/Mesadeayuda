import { useMemo, useState } from 'react';
import { LuCheck } from 'react-icons/lu';
import { api } from '../api';
import { ErrorCaja, Estado, MenuDesplegable, Vacio } from '../componentes/basicos';
import { useAccionesSolicitud } from '../estado/acciones';
import { activos, useCatalogos, useContadores } from '../estado/contextos';
import type { Pagina, SolicitudFila } from '../tipos';
import { fechaCorta, hora, minutosDesde, numero, tiempoDesde } from '../util/formato';
import { useAhora, useCarga } from '../util/hooks';
import { cambiarParams, Enlace, useUbicacion } from '../util/rutas';

/** Vista "Solicitudes pendientes": de la más antigua a la más reciente. */
export function Pendientes() {
  const catalogos = useCatalogos();
  const { contadores } = useContadores();
  const { cambiar, resolver } = useAccionesSolicitud();
  const { params } = useUbicacion();
  const ahora = useAhora(60_000);
  const [estadoFiltro, setEstadoFiltro] = useState<number | null>(null);
  const seleccion = Number(params.get('ver')) || null;

  const { datos, error, recargar } = useCarga(
    () => api.get<Pagina<SolicitudFila>>('/api/solicitudes?rapido=pendientes&orden=fecha&dir=asc&tamano=1000'),
    [],
    'solicitudes',
  );

  const porEstado = useMemo(() => {
    const mapa = new Map<number, { nombre: string; color: string | null; cantidad: number }>();
    datos?.items.forEach((s) => {
      if (s.estadoId === null) return;
      const actual = mapa.get(s.estadoId) ?? { nombre: s.estado ?? '', color: s.estadoColor, cantidad: 0 };
      actual.cantidad++;
      mapa.set(s.estadoId, actual);
    });
    return [...mapa.entries()];
  }, [datos]);

  const filas = datos?.items.filter((s) => estadoFiltro === null || s.estadoId === estadoFiltro) ?? [];

  return (
    <>
      <header className="encabezado">
        <div style={{ marginRight: 'auto' }}>
          <h1>Solicitudes pendientes</h1>
          <div className="encabezado-sub">Ordenadas de la que lleva más tiempo a la más reciente.</div>
        </div>
      </header>
      <div className="contenido">
        <div className="barra-filtros">
          <div className="pestanas" role="tablist" aria-label="Filtrar por estado">
            <button type="button" role="tab" aria-selected={estadoFiltro === null} className={`pestana${estadoFiltro === null ? ' activa' : ''}`} onClick={() => setEstadoFiltro(null)}>
              Todas <span className="cuenta">{numero(datos?.total ?? 0)}</span>
            </button>
            {porEstado.map(([idEstado, e]) => (
              <button key={idEstado} type="button" role="tab" aria-selected={estadoFiltro === idEstado} className={`pestana${estadoFiltro === idEstado ? ' activa' : ''}`} onClick={() => setEstadoFiltro(idEstado)}>
                {e.nombre} <span className="cuenta">{numero(e.cantidad)}</span>
              </button>
            ))}
          </div>
          {contadores && contadores.sinEstado > 0 && (
            <span className="tenue" style={{ marginLeft: 'auto', fontSize: 'var(--t-13)' }}>
              Además hay {numero(contadores.sinEstado)} solicitudes sin estado.{' '}
              <Enlace a="/solicitudes?rapido=sinestado">Revisarlas</Enlace>
            </span>
          )}
        </div>

        <ErrorCaja error={error} reintentar={recargar} />

        <div className="planilla-marco pendientes-lista">
          <table className="planilla">
            <thead>
              <tr>
                <th className="col-id">
                  <span>ID</span>
                </th>
                <th>
                  <span>Fecha</span>
                </th>
                <th>
                  <span>Funcionario</span>
                </th>
                <th>
                  <span>Problema</span>
                </th>
                <th>
                  <span>Responsable</span>
                </th>
                <th>
                  <span>Estado</span>
                </th>
                <th>
                  <span>Tiempo pendiente</span>
                </th>
                <th aria-label="Acciones" />
              </tr>
            </thead>
            <tbody>
              {filas.map((s) => {
                const minutos = minutosDesde(s.fechaIngreso, ahora);
                const claseTiempo = minutos > 7 * 24 * 60 ? 'largo' : minutos > 24 * 60 ? 'medio' : '';
                return (
                  <tr key={s.id} className={`pendiente${seleccion === s.id ? ' seleccionada' : ''}`} onClick={() => cambiarParams({ ver: s.id })}>
                    <td className="col-id">{s.id}</td>
                    <td className="col-fecha">
                      {fechaCorta(s.fechaIngreso)}
                      <span className="hora">{hora(s.fechaIngreso)}</span>
                    </td>
                    <td className="col-texto">
                      {s.nombreFuncionario}
                      {s.oficina && <div className="tenue">{s.oficina}</div>}
                    </td>
                    <td className="col-texto">
                      <strong style={{ fontWeight: 600 }}>{s.descripcion}</strong>
                      {s.observaciones && <div className="tenue recorte">{s.observaciones}</div>}
                    </td>
                    <td>
                      <MenuDesplegable
                        etiqueta={`Cambiar responsable de la solicitud ${s.id}`}
                        boton={s.responsable ?? <span className="tenue">Asignar</span>}
                        titulo="Cambiar responsable"
                        opciones={activos(catalogos.responsables).map((r) => ({
                          clave: r.id,
                          texto: r.nombre,
                          actual: r.id === s.responsableId,
                          alElegir: () => {
                            if (r.id !== s.responsableId) void cambiar(s.id, { responsableId: r.id }, `Solicitud #${s.id} asignada a ${r.nombre}.`, s.responsableId !== null ? { responsableId: s.responsableId } : undefined);
                          },
                        }))}
                      />
                    </td>
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
                            if (e.id !== s.estadoId) void cambiar(s.id, { estadoId: e.id }, `Solicitud #${s.id}: ${e.nombre}.`, s.estadoId !== null ? { estadoId: s.estadoId } : undefined);
                          },
                        }))}
                      />
                    </td>
                    <td>
                      <span className={`tiempo ${claseTiempo}`}>{tiempoDesde(s.fechaIngreso, ahora)}</span>
                    </td>
                    <td className="col-acciones">
                      <button
                        type="button"
                        className="btn btn-chico btn-ok"
                        onClick={(e) => {
                          e.stopPropagation();
                          void resolver(s);
                        }}
                      >
                        <LuCheck /> Resolver
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
          {datos && filas.length === 0 && (
            <Vacio titulo="No hay solicitudes pendientes">
              <p>Todo lo registrado está resuelto. Las nuevas solicitudes pendientes van a aparecer acá.</p>
            </Vacio>
          )}
        </div>
      </div>
    </>
  );
}
