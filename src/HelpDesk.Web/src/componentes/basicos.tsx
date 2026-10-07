import {
  Dialog,
  DialogBackdrop,
  DialogPanel,
  DialogTitle,
  Listbox,
  ListboxButton,
  ListboxOption,
  ListboxOptions,
  Menu,
  MenuButton,
  MenuItem,
  MenuItems,
} from '@headlessui/react';
import { Fragment, KeyboardEvent, ReactNode, useEffect, useId, useRef, useState } from 'react';
import { LuCheck, LuChevronDown, LuChevronLeft, LuChevronRight, LuLoaderCircle, LuX } from 'react-icons/lu';
import type { ItemCatalogo } from '../tipos';
import { normalizar, numero } from '../util/formato';

// ---------------------------------------------------------------- insignias

export function Estado({ nombre, esResuelto, color }: { nombre: string | null; esResuelto: boolean | null; color?: string | null }) {
  if (!nombre) return <span className="estado sin-estado">Sin estado</span>;
  const clase = `estado color-${color ?? (esResuelto ? 'verde' : 'ambar')}${esResuelto ? '' : ' pendiente'}`;
  return <span className={clase}>{nombre}</span>;
}

export function Prioridad({ nombre, nivel }: { nombre: string | null; nivel: number | null }) {
  if (!nombre) return <span className="tenue">—</span>;
  return <span className={nivel === 1 ? 'prioridad-1' : nivel !== null && nivel >= 3 ? 'prioridad-3' : ''}>{nombre}</span>;
}

// ---------------------------------------------------------------- estados de carga

export function Cargando({ texto = 'Cargando…' }: { texto?: string }) {
  return (
    <div className="cargando">
      <LuLoaderCircle className="girando" aria-hidden /> {texto}
    </div>
  );
}

export function ErrorCaja({ error, reintentar }: { error: { message: string } | null; reintentar?: () => void }) {
  if (!error) return null;
  return (
    <div className="error-caja" role="alert">
      {error.message}{' '}
      {reintentar && (
        <button type="button" className="btn btn-chico btn-sutil" onClick={reintentar}>
          Reintentar
        </button>
      )}
    </div>
  );
}

export function Vacio({ titulo, children }: { titulo: string; children?: ReactNode }) {
  return (
    <div className="vacio">
      <h3>{titulo}</h3>
      {children}
    </div>
  );
}

// ---------------------------------------------------------------- resaltado de búsqueda

/** Resalta las palabras buscadas sin distinguir tildes ni mayúsculas. */
export function Resaltado({ texto, palabras }: { texto: string | null | undefined; palabras: string[] }) {
  if (!texto) return null;
  const buscadas = palabras.map(normalizar).filter((p) => p.length >= 2);
  if (buscadas.length === 0) return <>{texto}</>;

  // Texto normalizado carácter a carácter, con el índice original de cada uno.
  let plano = '';
  const indices: number[] = [];
  for (let i = 0; i < texto.length; i++) {
    const n = texto[i].normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
    for (const c of n) {
      plano += c;
      indices.push(i);
    }
  }

  const marcas = new Array<boolean>(texto.length).fill(false);
  for (const palabra of buscadas) {
    let desde = 0;
    while (true) {
      const pos = plano.indexOf(palabra, desde);
      if (pos < 0) break;
      for (let k = pos; k < pos + palabra.length; k++) marcas[indices[k]] = true;
      desde = pos + palabra.length;
    }
  }

  const partes: ReactNode[] = [];
  let inicio = 0;
  for (let i = 1; i <= texto.length; i++) {
    if (i === texto.length || marcas[i] !== marcas[inicio]) {
      const trozo = texto.slice(inicio, i);
      partes.push(marcas[inicio] ? <mark key={inicio}>{trozo}</mark> : <Fragment key={inicio}>{trozo}</Fragment>);
      inicio = i;
    }
  }
  return <>{partes}</>;
}

// ---------------------------------------------------------------- selector de un clic

export function Segmentado({
  opciones,
  valor,
  onChange,
  etiqueta,
  permitirVacio = false,
  codigo = false,
  atajos = false,
}: {
  opciones: ItemCatalogo[];
  valor: number | null;
  onChange: (id: number | null) => void;
  etiqueta: string;
  permitirVacio?: boolean;
  codigo?: boolean;
  atajos?: boolean;
}) {
  const nombre = useId();
  const elegir = (id: number) => onChange(permitirVacio && id === valor ? null : id);
  return (
    <div role="radiogroup" aria-label={etiqueta} className={`seg${codigo ? ' seg-codigo' : ''}`}>
      {opciones.map((o, i) => (
        <label key={o.id} className={`seg-opcion${o.id === valor ? ' marcada' : ''}`} title={o.descripcion ?? undefined}>
          <input
            type="radio"
            name={nombre}
            value={o.id}
            checked={o.id === valor}
            onChange={() => elegir(o.id)}
            onClick={() => {
              if (o.id === valor && permitirVacio) onChange(null);
            }}
          />
          {o.color !== null && o.esResuelto !== null && <span className={`estado color-${o.color}`} aria-hidden />}
          {o.nombre}
          {atajos && i < 9 && <span className="atajo">{i + 1}</span>}
        </label>
      ))}
    </div>
  );
}

// ---------------------------------------------------------------- autocompletar

export interface Sugerencia {
  clave: string;
  texto: string;
  extra?: string;
  dato?: unknown;
}

export function Autocompletar({
  id,
  valor,
  onChange,
  buscar,
  onElegir,
  placeholder,
  minimo = 1,
  autoFocus,
  maxLength,
  invalido,
}: {
  id?: string;
  valor: string;
  onChange: (texto: string) => void;
  buscar: (texto: string) => Promise<Sugerencia[]>;
  onElegir?: (s: Sugerencia) => void;
  placeholder?: string;
  minimo?: number;
  autoFocus?: boolean;
  maxLength?: number;
  invalido?: boolean;
}) {
  const [sugerencias, setSugerencias] = useState<Sugerencia[]>([]);
  const [abierto, setAbierto] = useState(false);
  const [activa, setActiva] = useState(-1);
  const [enfocado, setEnfocado] = useState(false);
  const pedido = useRef(0);
  const idLista = useId();

  useEffect(() => {
    if (!enfocado) return;
    const texto = valor.trim();
    if (texto.length < minimo) {
      setSugerencias([]);
      return;
    }
    const numeroPedido = ++pedido.current;
    const t = setTimeout(() => {
      buscar(texto)
        .then((s) => {
          if (numeroPedido !== pedido.current) return;
          // No sugerir exactamente lo que ya está escrito.
          const filtradas = s.filter((x) => normalizar(x.texto) !== normalizar(texto) || x.extra);
          setSugerencias(filtradas);
          setActiva(-1);
        })
        .catch(() => {});
    }, 140);
    return () => clearTimeout(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [valor, enfocado, minimo]);

  const elegir = (s: Sugerencia) => {
    onChange(s.texto);
    onElegir?.(s);
    setAbierto(false);
    setActiva(-1);
  };

  const alPresionar = (e: KeyboardEvent<HTMLInputElement>) => {
    const visibles = abierto && sugerencias.length > 0;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setAbierto(true);
      setActiva((a) => Math.min(sugerencias.length - 1, a + 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActiva((a) => Math.max(-1, a - 1));
    } else if (e.key === 'Enter' && visibles && activa >= 0) {
      e.preventDefault();
      elegir(sugerencias[activa]);
    } else if (e.key === 'Tab' && visibles && activa >= 0) {
      elegir(sugerencias[activa]);
    } else if (e.key === 'Escape' && visibles) {
      e.preventDefault();
      e.stopPropagation();
      setAbierto(false);
    }
  };

  const mostrar = abierto && enfocado && sugerencias.length > 0;
  return (
    <div className="autocompletar">
      <input
        id={id}
        className="entrada"
        value={valor}
        placeholder={placeholder}
        autoComplete="off"
        autoFocus={autoFocus}
        maxLength={maxLength}
        aria-invalid={invalido || undefined}
        role="combobox"
        aria-expanded={mostrar}
        aria-controls={idLista}
        aria-autocomplete="list"
        aria-activedescendant={mostrar && activa >= 0 ? `${idLista}-${activa}` : undefined}
        onChange={(e) => {
          onChange(e.target.value);
          setAbierto(true);
        }}
        onFocus={() => {
          setEnfocado(true);
          setAbierto(true);
        }}
        onBlur={() => {
          setEnfocado(false);
          setAbierto(false);
        }}
        onKeyDown={alPresionar}
      />
      {mostrar && (
        <ul className="sugerencias" id={idLista} role="listbox">
          {sugerencias.map((s, i) => (
            <li
              key={s.clave}
              id={`${idLista}-${i}`}
              role="option"
              aria-selected={i === activa}
              className={`sugerencia${i === activa ? ' activa' : ''}`}
              onMouseDown={(e) => {
                e.preventDefault();
                elegir(s);
              }}
              onMouseEnter={() => setActiva(i)}
            >
              <span className="sugerencia-texto">
                <Resaltado texto={s.texto} palabras={valor.split(/\s+/)} />
              </span>
              {s.extra && <span className="sugerencia-extra">{s.extra}</span>}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ---------------------------------------------------------------- diálogos

export function Dialogo({
  abierto,
  onCerrar,
  titulo,
  children,
  pie,
  clase = '',
  cabecera,
}: {
  abierto: boolean;
  onCerrar: () => void;
  titulo?: ReactNode;
  children: ReactNode;
  pie?: ReactNode;
  clase?: string;
  cabecera?: ReactNode;
}) {
  return (
    <Dialog open={abierto} onClose={onCerrar}>
      <DialogBackdrop className="velo" />
      <div className="dialogo-capa">
        <DialogPanel className={`dialogo ${clase}`}>
          {cabecera ?? (
            <div className="dialogo-cabecera">
              <DialogTitle as="h2">{titulo}</DialogTitle>
              <button type="button" className="btn btn-sutil btn-icono btn-chico" onClick={onCerrar} aria-label="Cerrar">
                <LuX />
              </button>
            </div>
          )}
          {children}
          {pie && <div className="dialogo-pie">{pie}</div>}
        </DialogPanel>
      </div>
    </Dialog>
  );
}

export function PanelLateral({ abierto, onCerrar, children, etiqueta }: { abierto: boolean; onCerrar: () => void; children: ReactNode; etiqueta: string }) {
  return (
    <Dialog open={abierto} onClose={onCerrar}>
      <DialogBackdrop className="velo" />
      <div className="panel-capa">
        <DialogPanel className="panel-lateral" aria-label={etiqueta}>
          {children}
        </DialogPanel>
      </div>
    </Dialog>
  );
}

/** Confirmación simple con botón de acción. */
export function Confirmar({
  abierto,
  titulo,
  texto,
  accion,
  peligro,
  onCancelar,
  onConfirmar,
}: {
  abierto: boolean;
  titulo: string;
  texto: ReactNode;
  accion: string;
  peligro?: boolean;
  onCancelar: () => void;
  onConfirmar: () => void;
}) {
  return (
    <Dialogo
      abierto={abierto}
      onCerrar={onCancelar}
      titulo={titulo}
      pie={
        <>
          <button type="button" className="btn btn-sutil" onClick={onCancelar}>
            Cancelar
          </button>
          <button type="button" className={`btn ${peligro ? 'btn-peligro' : 'btn-primario'}`} onClick={onConfirmar}>
            {accion}
          </button>
        </>
      }
    >
      <div className="dialogo-cuerpo">{texto}</div>
    </Dialogo>
  );
}

// ---------------------------------------------------------------- menús

export interface OpcionMenu {
  clave: string | number;
  texto: ReactNode;
  actual?: boolean;
  alElegir: () => void;
}

/** Menú desplegable (por ejemplo, cambiar estado desde la fila de la tabla). */
export function MenuDesplegable({ boton, titulo, opciones, claseBoton = 'celda-menu', etiqueta }: { boton: ReactNode; titulo?: string; opciones: OpcionMenu[]; claseBoton?: string; etiqueta?: string }) {
  return (
    <div onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()} style={{ display: 'inline-block' }}>
      <Menu>
        <MenuButton className={claseBoton} aria-label={etiqueta}>
          {boton}
        </MenuButton>
        <MenuItems anchor="bottom start" className="menu-flotante" modal={false}>
          {titulo && <div className="menu-flotante-titulo">{titulo}</div>}
          {opciones.map((o) => (
            <MenuItem key={o.clave}>
              <button type="button" className={`menu-flotante-item${o.actual ? ' actual' : ''}`} onClick={o.alElegir}>
                <span style={{ width: 14, display: 'inline-grid' }}>{o.actual && <LuCheck />}</span>
                {o.texto}
              </button>
            </MenuItem>
          ))}
        </MenuItems>
      </Menu>
    </div>
  );
}

/** Selector múltiple para los filtros. "sin" representa "sin valor". */
export function SelectorMultiple({
  etiqueta,
  opciones,
  valores,
  onChange,
  incluirVacio,
}: {
  etiqueta: string;
  opciones: { id: number | 'sin'; nombre: string }[];
  valores: (number | 'sin')[];
  onChange: (v: (number | 'sin')[]) => void;
  incluirVacio?: string;
}) {
  const lista = incluirVacio ? [...opciones, { id: 'sin' as const, nombre: incluirVacio }] : opciones;
  const resumen =
    valores.length === 0
      ? 'Todos'
      : valores.length === 1
        ? lista.find((o) => o.id === valores[0])?.nombre ?? '1 elegido'
        : `${valores.length} elegidos`;
  return (
    <div className="campo">
      <span className="campo-etiqueta">{etiqueta}</span>
      <Listbox value={valores} onChange={onChange} multiple>
        <div className="multi">
          <ListboxButton className="multi-boton">
            <span className={valores.length === 0 ? 'tenue' : ''}>{resumen}</span>
            <LuChevronDown aria-hidden />
          </ListboxButton>
          <ListboxOptions anchor="bottom start" className="multi-opciones" style={{ width: 'var(--button-width)' }}>
            {lista.map((o) => (
              <ListboxOption key={String(o.id)} value={o.id} className="multi-opcion">
                {({ selected }) => (
                  <>
                    <span className="casilla">{selected && <LuCheck />}</span>
                    <span className={o.id === 'sin' ? 'tenue' : ''}>{o.nombre}</span>
                  </>
                )}
              </ListboxOption>
            ))}
          </ListboxOptions>
        </div>
      </Listbox>
    </div>
  );
}

// ---------------------------------------------------------------- paginador

export function Paginador({ pagina, tamano, total, onCambiar }: { pagina: number; tamano: number; total: number; onCambiar: (p: number) => void }) {
  const paginas = Math.max(1, Math.ceil(total / tamano));
  const desde = total === 0 ? 0 : (pagina - 1) * tamano + 1;
  const hasta = Math.min(total, pagina * tamano);
  return (
    <div className="paginador">
      <span className="num">
        {numero(desde)}–{numero(hasta)} de {numero(total)}
      </span>
      <button type="button" className="btn btn-chico btn-icono" disabled={pagina <= 1} onClick={() => onCambiar(pagina - 1)} aria-label="Página anterior">
        <LuChevronLeft />
      </button>
      <button type="button" className="btn btn-chico btn-icono" disabled={pagina >= paginas} onClick={() => onCambiar(pagina + 1)} aria-label="Página siguiente">
        <LuChevronRight />
      </button>
    </div>
  );
}
