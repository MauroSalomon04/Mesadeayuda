import { LuCircleAlert } from 'react-icons/lu';
import { api, query } from '../api';
import { Cargando, ErrorCaja } from '../componentes/basicos';
import { BarrasHorizontales, ColumnasMes } from '../componentes/Graficos';
import { useCatalogos, useUsuario } from '../estado/contextos';
import type { Conteo, Dashboard as DatosDashboard, ItemCatalogo } from '../tipos';
import { duracion, fechaCorta, numero } from '../util/formato';
import { useCarga } from '../util/hooks';
import { cambiarParams, Enlace, navegar, useUbicacion } from '../util/rutas';

const PERIODOS = [
  { clave: 'todo', texto: 'Todo el registro' },
  { clave: 'anio', texto: 'Este año' },
  { clave: '12m', texto: 'Últimos 12 meses' },
  { clave: '90d', texto: 'Últimos 90 días' },
  { clave: 'mes', texto: 'Este mes' },
] as const;

export function Dashboard() {
  const usuario = useUsuario();
  const catalogos = useCatalogos();
  const { params } = useUbicacion();
  const periodo = params.get('periodo') ?? 'todo';
  const { datos, error, recargar } = useCarga(() => api.get<DatosDashboard>(`/api/dashboard${query({ periodo })}`), [periodo], 'solicitudes');

  const filtrarPor = (lista: ItemCatalogo[], parametro: string) => (c: Conteo) => {
    const item = lista.find((i) => i.nombre === c.etiqueta);
    const desde = datos?.desde ? datos.desde.slice(0, 10) : null;
    navegar(`/solicitudes${query({ [parametro]: item ? item.id : 'sin', desde })}`);
  };

  const pie = datos?.desde ? `Desde el ${fechaCorta(datos.desde)}` : 'Todo el registro';

  return (
    <>
      <header className="encabezado">
        <div style={{ marginRight: 'auto' }}>
          <h1>Hola, {usuario.nombre.split(' ')[0]}</h1>
          <div className="encabezado-sub">Resumen de la mesa de ayuda de IntegradoC.</div>
        </div>
        <div className="pestanas" role="tablist" aria-label="Período de los gráficos">
          {PERIODOS.map((p) => (
            <button
              key={p.clave}
              type="button"
              role="tab"
              aria-selected={periodo === p.clave}
              className={`pestana${periodo === p.clave ? ' activa' : ''}`}
              onClick={() => cambiarParams({ periodo: p.clave === 'todo' ? null : p.clave }, true)}
            >
              {p.texto}
            </button>
          ))}
        </div>
      </header>

      <div className="contenido">
        <ErrorCaja error={error} reintentar={recargar} />
        {!datos && !error && <Cargando />}
        {datos && (
          <>
            {datos.indicadores.sinEstado > 0 && (
              <div className="aviso-datos">
                <LuCircleAlert aria-hidden style={{ color: 'var(--wait)', fontSize: 18, flex: 'none' }} />
                <span>
                  Hay <strong>{numero(datos.indicadores.sinEstado)}</strong> solicitudes del registro histórico sin estado. No se cuentan como pendientes ni como resueltas.
                </span>
                <Enlace a="/solicitudes?rapido=sinestado" className="btn btn-chico" style={{ marginLeft: 'auto' }}>
                  Revisarlas
                </Enlace>
              </div>
            )}

            <section className="indicadores" aria-label="Indicadores">
              <Enlace a="/solicitudes?rapido=hoy" className="indicador">
                <div className="indicador-etiqueta">Hoy</div>
                <div className="indicador-valor">{numero(datos.indicadores.hoy)}</div>
                <div className="indicador-nota">solicitudes ingresadas</div>
              </Enlace>
              <div className="indicador">
                <div className="indicador-etiqueta">Esta semana</div>
                <div className="indicador-valor">{numero(datos.indicadores.semana)}</div>
                <div className="indicador-nota">desde el lunes</div>
              </div>
              <div className="indicador">
                <div className="indicador-etiqueta">Este mes</div>
                <div className="indicador-valor">{numero(datos.indicadores.mes)}</div>
                <div className="indicador-nota">desde el día 1</div>
              </div>
              <Enlace a="/pendientes" className={`indicador${datos.indicadores.pendientes > 0 ? ' destacado' : ''}`}>
                <div className="indicador-etiqueta">Pendientes</div>
                <div className="indicador-valor">{numero(datos.indicadores.pendientes)}</div>
                <div className="indicador-nota">a la fecha</div>
              </Enlace>
              <div className="indicador">
                <div className="indicador-etiqueta">Resueltas</div>
                <div className="indicador-valor">{numero(datos.enPeriodo.resueltas)}</div>
                <div className="indicador-nota">de {numero(datos.enPeriodo.total)} en el período</div>
              </div>
              <div className="indicador">
                <div className="indicador-etiqueta">Tiempo de resolución</div>
                <div className="indicador-valor">{datos.enPeriodo.promedioResolucionMin !== null ? duracion(datos.enPeriodo.promedioResolucionMin) : '—'}</div>
                <div className="indicador-nota">
                  {datos.enPeriodo.resueltasConTiempo > 0
                    ? `promedio de ${numero(datos.enPeriodo.resueltasConTiempo)} que quedaron pendientes`
                    : 'se calcula con las resueltas desde la app'}
                </div>
              </div>
            </section>

            <div className="tableros">
              <section className="tablero ancho-8">
                <h2>Solicitudes por mes</h2>
                <p className="tablero-sub">
                  {pie}. Duración estimada promedio: {datos.enPeriodo.duracionPromedioMin !== null ? duracion(datos.enPeriodo.duracionPromedioMin) : '—'} por solicitud,{' '}
                  {duracion(datos.enPeriodo.minutosEstimados)} en total.
                </p>
                <ColumnasMes datos={datos.porMes} />
              </section>

              <section className="tablero" style={{ gridRow: 'span 2' }}>
                <h2>Problemas más frecuentes</h2>
                <p className="tablero-sub">Descripciones iguales o casi iguales. Clic para ver los casos.</p>
                <ol className="ranking">
                  {datos.topProblemas.map((p, i) => (
                    <li key={p.etiqueta}>
                      <span className="posicion">{i + 1}</span>
                      <Enlace a={`/solicitudes${query({ q: p.etiqueta })}`}>{p.etiqueta}</Enlace>
                      <span className="cantidad">{numero(p.cantidad)}</span>
                      <span className="pista" aria-hidden>
                        <span style={{ width: `${(100 * p.cantidad) / (datos.topProblemas[0]?.cantidad || 1)}%` }} />
                      </span>
                    </li>
                  ))}
                </ol>
              </section>

              <section className="tablero ancho-8">
                <h2>Por oficina</h2>
                <p className="tablero-sub">Las 10 oficinas con más solicitudes. {pie}.</p>
                <BarrasHorizontales
                  datos={datos.porOficina}
                  total={datos.enPeriodo.total}
                  alElegir={(c) => navegar(`/solicitudes${query({ ofi: c.etiqueta.startsWith('(') ? null : c.etiqueta, desde: datos.desde?.slice(0, 10) })}`)}
                />
              </section>

              <section className="tablero">
                <h2>Por tipo de solicitud</h2>
                <p className="tablero-sub">{pie}.</p>
                <BarrasHorizontales datos={datos.porTipo} alElegir={filtrarPor(catalogos.tipos, 'tipo')} />
              </section>

              <section className="tablero">
                <h2>Por responsable</h2>
                <p className="tablero-sub">{pie}.</p>
                <BarrasHorizontales datos={datos.porResponsable} alElegir={filtrarPor(catalogos.responsables, 'responsable')} />
              </section>

              <section className="tablero">
                <h2>Por medio de contacto</h2>
                <p className="tablero-sub">{pie}.</p>
                <BarrasHorizontales datos={datos.porMedio} alElegir={filtrarPor(catalogos.medios, 'medio')} />
              </section>

              <section className="tablero">
                <h2>Por prioridad</h2>
                <p className="tablero-sub">{pie}.</p>
                <BarrasHorizontales datos={datos.porPrioridad} alElegir={filtrarPor(catalogos.prioridades, 'prioridad')} />
              </section>

              <section className="tablero">
                <h2>Por estado</h2>
                <p className="tablero-sub">{pie}.</p>
                <BarrasHorizontales datos={datos.porEstado} alElegir={filtrarPor(catalogos.estados, 'estado')} />
              </section>
            </div>
          </>
        )}
      </div>
    </>
  );
}
