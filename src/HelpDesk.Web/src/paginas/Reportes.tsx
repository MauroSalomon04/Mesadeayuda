import { LuDownload } from 'react-icons/lu';
import { api, descargar, query } from '../api';
import { ErrorCaja, SelectorMultiple, Vacio } from '../componentes/basicos';
import { useCatalogos } from '../estado/contextos';
import type { Reporte } from '../tipos';
import { duracion, numero } from '../util/formato';
import { useCarga } from '../util/hooks';
import { cambiarParams, useUbicacion } from '../util/rutas';

const DIMENSIONES = [
  { clave: 'mes', texto: 'Mes' },
  { clave: 'dia', texto: 'Día de la semana' },
  { clave: 'hora', texto: 'Hora de ingreso' },
  { clave: 'responsable', texto: 'Responsable' },
  { clave: 'tipo', texto: 'Tipo de solicitud' },
  { clave: 'oficina', texto: 'Oficina' },
  { clave: 'medio', texto: 'Medio de contacto' },
  { clave: 'prioridad', texto: 'Prioridad' },
  { clave: 'estado', texto: 'Estado' },
];

function lista(valor: string | null): (number | 'sin')[] {
  return (valor ?? '')
    .split(',')
    .filter(Boolean)
    .map((v) => (v === 'sin' ? 'sin' : Number(v)));
}

export function Reportes() {
  const catalogos = useCatalogos();
  const { params } = useUbicacion();
  const dimension = params.get('dimension') ?? 'responsable';
  const dimension2 = params.get('dimension2') ?? '';
  const metrica = params.get('metrica') ?? 'cantidad';
  const desde = params.get('desde') ?? '';
  const hasta = params.get('hasta') ?? '';

  const consulta = query({
    dimension,
    dimension2,
    desde,
    hasta,
    responsable: params.get('responsable'),
    tipo: params.get('tipo'),
    estado: params.get('estado'),
    medio: params.get('medio'),
  });
  const { datos, error, cargando, recargar } = useCarga(() => api.get<Reporte>(`/api/reportes${consulta}`), [consulta], 'solicitudes');
  const enMinutos = metrica === 'minutos';
  const valor = (cantidad: number, minutos: number) => (enMinutos ? duracion(minutos) || '0 min' : numero(cantidad));

  return (
    <>
      <header className="encabezado">
        <div style={{ marginRight: 'auto' }}>
          <h1>Reportes</h1>
          <div className="encabezado-sub">Cantidad de solicitudes o minutos de trabajo estimados, agrupados como necesites.</div>
        </div>
        <button type="button" className="btn" onClick={() => descargar(`/api/reportes/exportar${consulta}&metrica=${metrica}`)}>
          <LuDownload /> Exportar a Excel
        </button>
      </header>

      <div className="contenido">
        <div className="filtros-avanzados">
          <div className="campo">
            <label htmlFor="r-dim">Agrupar por</label>
            <select id="r-dim" className="selector" value={dimension} onChange={(e) => cambiarParams({ dimension: e.target.value }, true)}>
              {DIMENSIONES.map((d) => (
                <option key={d.clave} value={d.clave}>
                  {d.texto}
                </option>
              ))}
            </select>
          </div>
          <div className="campo">
            <label htmlFor="r-dim2">Y en columnas</label>
            <select id="r-dim2" className="selector" value={dimension2} onChange={(e) => cambiarParams({ dimension2: e.target.value || null }, true)}>
              <option value="">Nada (solo totales)</option>
              {DIMENSIONES.filter((d) => d.clave !== dimension).map((d) => (
                <option key={d.clave} value={d.clave}>
                  {d.texto}
                </option>
              ))}
            </select>
          </div>
          <div className="campo">
            <label htmlFor="r-metrica">Medida</label>
            <select id="r-metrica" className="selector" value={metrica} onChange={(e) => cambiarParams({ metrica: e.target.value === 'cantidad' ? null : e.target.value }, true)}>
              <option value="cantidad">Solicitudes</option>
              <option value="minutos">Minutos estimados</option>
            </select>
          </div>
          <div className="campo">
            <label htmlFor="r-desde">Desde</label>
            <input id="r-desde" type="date" className="entrada" value={desde} onChange={(e) => cambiarParams({ desde: e.target.value || null }, true)} />
          </div>
          <div className="campo">
            <label htmlFor="r-hasta">Hasta</label>
            <input id="r-hasta" type="date" className="entrada" value={hasta} onChange={(e) => cambiarParams({ hasta: e.target.value || null }, true)} />
          </div>
          <SelectorMultiple
            etiqueta="Responsable"
            opciones={catalogos.responsables.map((r) => ({ id: r.id, nombre: r.nombre }))}
            valores={lista(params.get('responsable'))}
            incluirVacio="Sin responsable"
            onChange={(v) => cambiarParams({ responsable: v.join(',') || null }, true)}
          />
          <SelectorMultiple
            etiqueta="Tipo"
            opciones={catalogos.tipos.map((r) => ({ id: r.id, nombre: r.nombre }))}
            valores={lista(params.get('tipo'))}
            incluirVacio="Sin tipo"
            onChange={(v) => cambiarParams({ tipo: v.join(',') || null }, true)}
          />
          <SelectorMultiple
            etiqueta="Estado"
            opciones={catalogos.estados.map((r) => ({ id: r.id, nombre: r.nombre }))}
            valores={lista(params.get('estado'))}
            incluirVacio="Sin estado"
            onChange={(v) => cambiarParams({ estado: v.join(',') || null }, true)}
          />
        </div>

        <ErrorCaja error={error} reintentar={recargar} />

        {datos && datos.filas.length === 0 && <Vacio titulo="No hay solicitudes con estos filtros" />}
        {datos && datos.filas.length > 0 && (
          <div className="planilla-marco" aria-busy={cargando} style={{ maxHeight: 'none' }}>
            <table className="tabla-simple">
              <thead>
                <tr>
                  <th>{datos.dimensionEtiqueta}</th>
                  {datos.columnas.length > 0 ? (
                    <>
                      {datos.columnas.map((c) => (
                        <th key={c} className="col-num">
                          {c}
                        </th>
                      ))}
                      <th className="col-num">Total</th>
                    </>
                  ) : (
                    <>
                      <th className="col-num">Solicitudes</th>
                      <th className="col-num">% del total</th>
                      <th className="col-num">Minutos estimados</th>
                      <th className="col-num">Promedio por solicitud</th>
                    </>
                  )}
                </tr>
              </thead>
              <tbody>
                {datos.filas.map((f) => (
                  <tr key={f.clave}>
                    <td>{f.etiqueta}</td>
                    {datos.columnas.length > 0 ? (
                      <>
                        {f.celdasCantidad.map((c, i) => (
                          <td key={i} className="col-num">
                            {c || f.celdasMinutos[i] ? valor(c, f.celdasMinutos[i]) : <span className="tenue">·</span>}
                          </td>
                        ))}
                        <td className="col-num">
                          <strong>{valor(f.cantidad, f.minutos)}</strong>
                        </td>
                      </>
                    ) : (
                      <>
                        <td className="col-num">{numero(f.cantidad)}</td>
                        <td className="col-num">{datos.totales.cantidad ? ((100 * f.cantidad) / datos.totales.cantidad).toFixed(1).replace('.', ',') : '0'} %</td>
                        <td className="col-num">{duracion(f.minutos) || '0 min'}</td>
                        <td className="col-num">{f.promedioMin !== null ? duracion(f.promedioMin) : '—'}</td>
                      </>
                    )}
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td>Total</td>
                  {datos.columnas.length > 0 ? (
                    <>
                      {datos.totales.celdasCantidad.map((c, i) => (
                        <td key={i} className="col-num">
                          {valor(c, datos.totales.celdasMinutos[i])}
                        </td>
                      ))}
                      <td className="col-num">{valor(datos.totales.cantidad, datos.totales.minutos)}</td>
                    </>
                  ) : (
                    <>
                      <td className="col-num">{numero(datos.totales.cantidad)}</td>
                      <td className="col-num">100 %</td>
                      <td className="col-num">{duracion(datos.totales.minutos) || '0 min'}</td>
                      <td className="col-num">{datos.totales.promedioMin !== null ? duracion(datos.totales.promedioMin) : '—'}</td>
                    </>
                  )}
                </tr>
              </tfoot>
            </table>
          </div>
        )}
        <p className="tenue" style={{ fontSize: 'var(--t-12)', marginTop: 10 }}>
          Los minutos son la duración estimada que se anota en cada solicitud. Las solicitudes sin duración cuentan como 0 minutos.
        </p>
      </div>
    </>
  );
}
