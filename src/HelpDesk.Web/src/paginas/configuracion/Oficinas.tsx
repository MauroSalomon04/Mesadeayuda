import { useMemo, useState } from 'react';
import { LuMerge, LuPencil, LuSearch } from 'react-icons/lu';
import { api, query } from '../../api';
import { Dialogo, ErrorCaja } from '../../componentes/basicos';
import { mensajeError, useAvisar } from '../../estado/contextos';
import type { OficinaItem } from '../../tipos';
import { fechaCorta, normalizar, numero, plural } from '../../util/formato';
import { emitir, useCarga, useDebounce } from '../../util/hooks';
import { navegar } from '../../util/rutas';

/** Las oficinas se crean solas al registrar solicitudes. Acá se corrigen nombres y se unifican duplicadas. */
export function Oficinas() {
  const [texto, setTexto] = useState('');
  const diferido = useDebounce(texto, 250);
  const { datos, error, recargar } = useCarga(() => api.get<OficinaItem[]>(`/api/admin/oficinas`), [], 'catalogos');
  const [renombrando, setRenombrando] = useState<OficinaItem | null>(null);
  const [unificando, setUnificando] = useState<OficinaItem | null>(null);

  const filtradas = useMemo(() => {
    const buscado = normalizar(diferido);
    return (datos ?? []).filter((o) => !buscado || normalizar(o.nombre).includes(buscado));
  }, [datos, diferido]);

  return (
    <div className="tarjeta">
      <p className="tenue" style={{ marginTop: 0, maxWidth: 760 }}>
        Cada oficina escrita en una solicitud queda guardada para sugerirla después. Si la misma oficina aparece con nombres distintos
        (por ejemplo “RRHH - Hosp Español” y “RRHH Hospital español”), unificalas para que las estadísticas las cuenten juntas.
      </p>
      <div className="barra-filtros">
        <div className="buscador" style={{ maxWidth: 420 }}>
          <LuSearch aria-hidden />
          <input type="search" value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Buscar oficina" aria-label="Buscar oficina" style={{ height: 38 }} />
        </div>
        {datos && <span className="tenue">{plural(filtradas.length, 'oficina')}</span>}
      </div>
      <ErrorCaja error={error} reintentar={recargar} />
      <div className="planilla-marco" style={{ maxHeight: 'calc(100vh - 300px)' }}>
        <table className="tabla-simple">
          <thead>
            <tr>
              <th>Oficina</th>
              <th className="col-num">Solicitudes</th>
              <th>Última solicitud</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {filtradas.map((o) => (
              <tr key={o.id} className={o.activo ? '' : 'inactivo'}>
                <td>
                  <button type="button" className="celda-menu" onClick={() => navegar(`/solicitudes${query({ ofi: o.nombre })}`)} title="Ver sus solicitudes">
                    {o.nombre}
                  </button>
                  {!o.activo && <span className="etiqueta-estado" style={{ marginLeft: 8 }}>Desactivada</span>}
                </td>
                <td className="col-num">{numero(o.solicitudes)}</td>
                <td>{o.ultimaSolicitud ? fechaCorta(o.ultimaSolicitud) : <span className="tenue">—</span>}</td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                  <button type="button" className="btn btn-chico btn-sutil" onClick={() => setRenombrando(o)}>
                    <LuPencil /> Editar
                  </button>
                  <button type="button" className="btn btn-chico btn-sutil" onClick={() => setUnificando(o)}>
                    <LuMerge /> Unificar
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {renombrando && <Renombrar oficina={renombrando} onCerrar={() => setRenombrando(null)} />}
      {unificando && datos && <Unificar origen={unificando} oficinas={datos} onCerrar={() => setUnificando(null)} />}
    </div>
  );
}

function Renombrar({ oficina, onCerrar }: { oficina: OficinaItem; onCerrar: () => void }) {
  const avisar = useAvisar();
  const [nombre, setNombre] = useState(oficina.nombre);
  const [activo, setActivo] = useState(oficina.activo);
  const [error, setError] = useState<string | null>(null);

  const guardar = async () => {
    try {
      await api.put(`/api/admin/oficinas/${oficina.id}`, { nombre, activo });
      emitir('catalogos');
      avisar('Oficina actualizada.');
      onCerrar();
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Dialogo
      abierto
      onCerrar={onCerrar}
      titulo="Editar oficina"
      pie={
        <>
          <button type="button" className="btn btn-sutil" onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primario" onClick={() => void guardar()}>
            Guardar
          </button>
        </>
      }
    >
      <div className="dialogo-cuerpo" style={{ display: 'grid', gap: 12 }}>
        <div className="campo">
          <label htmlFor="o-nombre">Nombre</label>
          <input id="o-nombre" className="entrada" value={nombre} onChange={(e) => setNombre(e.target.value)} maxLength={150} autoFocus />
          <span className="campo-ayuda">El cambio se refleja en sus {numero(oficina.solicitudes)} solicitudes y queda en el historial de cada una.</span>
        </div>
        <label className="seg-opcion" style={{ width: 'fit-content' }}>
          <input type="checkbox" checked={activo} onChange={(e) => setActivo(e.target.checked)} />
          <span className="casilla">{activo && '✓'}</span>
          Sugerirla al escribir solicitudes
        </label>
        {error && <div className="error-caja">{error}</div>}
      </div>
    </Dialogo>
  );
}

function Unificar({ origen, oficinas, onCerrar }: { origen: OficinaItem; oficinas: OficinaItem[]; onCerrar: () => void }) {
  const avisar = useAvisar();
  const [texto, setTexto] = useState('');
  const [destino, setDestino] = useState<OficinaItem | null>(null);
  const [error, setError] = useState<string | null>(null);

  const candidatas = useMemo(() => {
    const buscado = normalizar(texto);
    const palabras = normalizar(origen.nombre).split(' ').filter((p) => p.length > 3);
    return oficinas
      .filter((o) => o.id !== origen.id)
      .filter((o) => (buscado ? normalizar(o.nombre).includes(buscado) : palabras.some((p) => normalizar(o.nombre).includes(p))))
      .slice(0, 12);
  }, [texto, oficinas, origen]);

  const unificar = async () => {
    if (!destino) return setError('Elegí la oficina que queda.');
    try {
      const r = await api.post<{ solicitudesMovidas: number }>(`/api/admin/oficinas/${origen.id}/unificar`, { destinoId: destino.id });
      emitir('catalogos');
      emitir('solicitudes');
      avisar(`Se movieron ${plural(r.solicitudesMovidas, 'solicitud', 'solicitudes')} a “${destino.nombre}”.`);
      onCerrar();
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Dialogo
      abierto
      onCerrar={onCerrar}
      titulo="Unificar oficinas"
      pie={
        <>
          <button type="button" className="btn btn-sutil" onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primario" disabled={!destino} onClick={() => void unificar()}>
            Unificar
          </button>
        </>
      }
    >
      <div className="dialogo-cuerpo" style={{ display: 'grid', gap: 12 }}>
        <p style={{ margin: 0 }}>
          Las {numero(origen.solicitudes)} solicitudes de <strong>{origen.nombre}</strong> pasan a la oficina que elijas, y “{origen.nombre}” deja de existir.
        </p>
        <div className="campo">
          <label htmlFor="u-buscar">Oficina que queda</label>
          <input id="u-buscar" className="entrada" value={texto} onChange={(e) => setTexto(e.target.value)} placeholder="Buscar" autoFocus />
        </div>
        <div className="seg" style={{ flexDirection: 'column', alignItems: 'stretch' }}>
          {candidatas.map((o) => (
            <label key={o.id} className={`seg-opcion${destino?.id === o.id ? ' marcada' : ''}`} style={{ justifyContent: 'space-between' }}>
              <input type="radio" name="destino" checked={destino?.id === o.id} onChange={() => setDestino(o)} />
              <span>{o.nombre}</span>
              <span className="tenue">{plural(o.solicitudes, 'solicitud', 'solicitudes')}</span>
            </label>
          ))}
          {candidatas.length === 0 && <span className="tenue">No hay oficinas parecidas. Buscala por nombre.</span>}
        </div>
        {error && <div className="error-caja">{error}</div>}
      </div>
    </Dialogo>
  );
}
