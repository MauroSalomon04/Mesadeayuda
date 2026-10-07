import { useState } from 'react';
import { LuPlus } from 'react-icons/lu';
import { api } from '../../api';
import { Dialogo, ErrorCaja, Estado } from '../../componentes/basicos';
import { mensajeError, useAvisar } from '../../estado/contextos';
import type { ItemCatalogo } from '../../tipos';
import { numero } from '../../util/formato';
import { emitir, useCarga } from '../../util/hooks';

const COLORES = [
  { clave: 'verde', texto: 'Verde' },
  { clave: 'ambar', texto: 'Ámbar' },
  { clave: 'naranja', texto: 'Naranja' },
  { clave: 'rojo', texto: 'Rojo' },
  { clave: 'azul', texto: 'Azul' },
  { clave: 'violeta', texto: 'Violeta' },
  { clave: 'gris', texto: 'Gris' },
];

interface Config {
  singular: string;
  etiquetaNombre: string;
  predeterminado?: boolean;
  estado?: boolean;
  nivel?: boolean;
  descripcion?: boolean;
  usos: string;
}

const CONFIG: Record<string, Config> = {
  responsables: { singular: 'responsable', etiquetaNombre: 'Nombre', usos: 'Solicitudes' },
  tipos: { singular: 'tipo', etiquetaNombre: 'Código', descripcion: true, usos: 'Solicitudes' },
  medios: { singular: 'medio', etiquetaNombre: 'Nombre', predeterminado: true, usos: 'Solicitudes' },
  estados: { singular: 'estado', etiquetaNombre: 'Nombre', predeterminado: true, estado: true, usos: 'Solicitudes' },
  prioridades: { singular: 'prioridad', etiquetaNombre: 'Nombre', predeterminado: true, nivel: true, usos: 'Solicitudes' },
  areas: { singular: 'área', etiquetaNombre: 'Nombre', usos: 'Tareas extra' },
};

export function Catalogo({ clave }: { clave: string }) {
  const config = CONFIG[clave];
  const { datos, error, recargar } = useCarga(() => api.get<ItemCatalogo[]>(`/api/admin/catalogos/${clave}`), [clave], 'catalogos');
  const [editando, setEditando] = useState<ItemCatalogo | 'nuevo' | null>(null);

  return (
    <div className="tarjeta" style={{ maxWidth: 920 }}>
      <div style={{ display: 'flex', alignItems: 'center', marginBottom: 12 }}>
        <span className="tenue">{datos ? `${datos.filter((d) => d.activo).length} activos de ${datos.length}` : ''}</span>
        <button type="button" className="btn btn-primario" style={{ marginLeft: 'auto' }} onClick={() => setEditando('nuevo')}>
          <LuPlus /> Agregar {config.singular}
        </button>
      </div>
      <ErrorCaja error={error} reintentar={recargar} />
      <table className="tabla-simple">
        <thead>
          <tr>
            <th>{config.etiquetaNombre}</th>
            {config.descripcion && <th>Descripción</th>}
            {config.estado && <th>Cierra la solicitud</th>}
            {config.nivel && <th className="col-num">Nivel</th>}
            {config.predeterminado && <th>Predeterminado</th>}
            <th className="col-num">Orden</th>
            <th>Situación</th>
            <th className="col-num">{config.usos}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {datos?.map((item) => (
            <tr key={item.id} className={item.activo ? '' : 'inactivo'}>
              <td>{config.estado ? <Estado nombre={item.nombre} esResuelto={item.esResuelto} color={item.color} /> : <strong className={clave === 'tipos' ? 'codigo' : ''}>{item.nombre}</strong>}</td>
              {config.descripcion && <td>{item.descripcion ?? <span className="tenue">—</span>}</td>}
              {config.estado && <td>{item.esResuelto ? 'Sí (resuelto)' : 'No (pendiente)'}</td>}
              {config.nivel && <td className="col-num">{item.nivel}</td>}
              {config.predeterminado && <td>{item.predeterminado ? <span className="etiqueta-estado ok">Predeterminado</span> : ''}</td>}
              <td className="col-num">{item.orden}</td>
              <td>{item.activo ? <span className="etiqueta-estado ok">Activo</span> : <span className="etiqueta-estado">Desactivado</span>}</td>
              <td className="col-num">{numero(item.usos ?? 0)}</td>
              <td style={{ textAlign: 'right' }}>
                <button type="button" className="btn btn-chico" onClick={() => setEditando(item)}>
                  Editar
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editando && <EditorItem clave={clave} config={config} item={editando === 'nuevo' ? null : editando} onCerrar={() => setEditando(null)} />}
    </div>
  );
}

function EditorItem({ clave, config, item, onCerrar }: { clave: string; config: Config; item: ItemCatalogo | null; onCerrar: () => void }) {
  const avisar = useAvisar();
  const [nombre, setNombre] = useState(item?.nombre ?? '');
  const [descripcion, setDescripcion] = useState(item?.descripcion ?? '');
  const [activo, setActivo] = useState(item?.activo ?? true);
  const [orden, setOrden] = useState(item ? String(item.orden) : '');
  const [predeterminado, setPredeterminado] = useState(item?.predeterminado ?? false);
  const [esResuelto, setEsResuelto] = useState(item?.esResuelto ?? false);
  const [color, setColor] = useState(item?.color ?? 'ambar');
  const [nivel, setNivel] = useState(item?.nivel ? String(item.nivel) : '');
  const [error, setError] = useState<string | null>(null);

  const guardar = async () => {
    setError(null);
    const cuerpo = {
      nombre,
      activo,
      orden: orden === '' ? null : Number(orden),
      predeterminado: config.predeterminado ? predeterminado : undefined,
      esResuelto: config.estado ? esResuelto : undefined,
      color: config.estado ? color : undefined,
      nivel: config.nivel ? (nivel === '' ? null : Number(nivel)) : undefined,
      descripcion: config.descripcion ? descripcion : undefined,
    };
    try {
      if (item) await api.put(`/api/admin/catalogos/${clave}/${item.id}`, cuerpo);
      else await api.post(`/api/admin/catalogos/${clave}`, cuerpo);
      emitir('catalogos');
      avisar(item ? 'Cambios guardados.' : `Se agregó “${nombre.trim()}”.`);
      onCerrar();
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Dialogo
      abierto
      onCerrar={onCerrar}
      titulo={item ? `Editar ${config.singular}` : `Agregar ${config.singular}`}
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
          <label htmlFor="c-nombre">{config.etiquetaNombre}</label>
          <input id="c-nombre" className="entrada" value={nombre} onChange={(e) => setNombre(e.target.value)} autoFocus />
          {item && item.usos ? <span className="campo-ayuda">Al renombrarlo, las {numero(item.usos)} solicitudes que lo usan muestran el nombre nuevo.</span> : null}
        </div>
        {config.descripcion && (
          <div className="campo">
            <label htmlFor="c-desc">Descripción (opcional)</label>
            <input id="c-desc" className="entrada" value={descripcion} onChange={(e) => setDescripcion(e.target.value)} maxLength={200} />
          </div>
        )}
        {config.estado && (
          <>
            <label className="seg-opcion" style={{ width: 'fit-content' }}>
              <input type="checkbox" checked={esResuelto} onChange={(e) => setEsResuelto(e.target.checked)} />
              <span className="casilla">{esResuelto && '✓'}</span>
              Este estado cierra la solicitud (resuelto)
            </label>
            <div className="campo">
              <label htmlFor="c-color">Color</label>
              <select id="c-color" className="selector" value={color} onChange={(e) => setColor(e.target.value)}>
                {COLORES.map((c) => (
                  <option key={c.clave} value={c.clave}>
                    {c.texto}
                  </option>
                ))}
              </select>
            </div>
          </>
        )}
        {config.nivel && (
          <div className="campo">
            <label htmlFor="c-nivel">Nivel (1 = más urgente)</label>
            <input id="c-nivel" className="entrada" inputMode="numeric" value={nivel} onChange={(e) => setNivel(e.target.value.replace(/\D/g, ''))} />
          </div>
        )}
        <div className="campo">
          <label htmlFor="c-orden">Orden en las listas</label>
          <input id="c-orden" className="entrada" inputMode="numeric" value={orden} onChange={(e) => setOrden(e.target.value.replace(/\D/g, ''))} placeholder="Al final" />
        </div>
        {config.predeterminado && (
          <label className="seg-opcion" style={{ width: 'fit-content' }}>
            <input type="checkbox" checked={predeterminado} onChange={(e) => setPredeterminado(e.target.checked)} />
            <span className="casilla">{predeterminado && '✓'}</span>
            Usar como valor predeterminado en solicitudes nuevas
          </label>
        )}
        <label className="seg-opcion" style={{ width: 'fit-content' }}>
          <input type="checkbox" checked={activo} onChange={(e) => setActivo(e.target.checked)} />
          <span className="casilla">{activo && '✓'}</span>
          Activo (aparece en los formularios)
        </label>
        {error && <div className="error-caja">{error}</div>}
      </div>
    </Dialogo>
  );
}
