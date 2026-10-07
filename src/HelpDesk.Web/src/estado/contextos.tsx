import { createContext, ReactNode, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import { alPerderSesion, api, ErrorApi } from '../api';
import type { Catalogos, Contadores, ItemCatalogo, Sesion } from '../tipos';
import { useEvento } from '../util/hooks';

// ================================================================ sesión

interface ValorSesion {
  usuario: Sesion | null;
  iniciando: boolean;
  ingresar: (usuario: string, password: string) => Promise<void>;
  salir: () => Promise<void>;
  actualizar: (sesion: Sesion) => void;
}

const ContextoSesion = createContext<ValorSesion | null>(null);

export function ProveedorSesion({ children }: { children: ReactNode }) {
  const [usuario, setUsuario] = useState<Sesion | null>(null);
  const [iniciando, setIniciando] = useState(true);

  useEffect(() => {
    api
      .get<Sesion>('/api/auth/yo', { ignorar401: true })
      .then(setUsuario)
      .catch(() => setUsuario(null))
      .finally(() => setIniciando(false));

    return alPerderSesion((error) => {
      if (error.estado === 401) setUsuario(null);
      else setUsuario((u) => (u ? { ...u, debeCambiarPassword: true } : u));
    });
  }, []);

  const ingresar = useCallback(async (nombre: string, password: string) => {
    const sesion = await api.post<Sesion>('/api/auth/login', { usuario: nombre, password });
    setUsuario(sesion);
  }, []);

  const salir = useCallback(async () => {
    try {
      await api.post('/api/auth/logout');
    } finally {
      setUsuario(null);
    }
  }, []);

  const valor = useMemo(() => ({ usuario, iniciando, ingresar, salir, actualizar: setUsuario }), [usuario, iniciando, ingresar, salir]);
  return <ContextoSesion.Provider value={valor}>{children}</ContextoSesion.Provider>;
}

export function useSesion() {
  const valor = useContext(ContextoSesion);
  if (!valor) throw new Error('useSesion fuera de ProveedorSesion');
  return valor;
}

/** Usuario autenticado (solo usar dentro de pantallas protegidas). */
export function useUsuario(): Sesion {
  const { usuario } = useSesion();
  if (!usuario) throw new Error('Sin sesión');
  return usuario;
}

// ================================================================ catálogos

export interface ValorCatalogos extends Catalogos {
  listo: boolean;
  recargar: () => void;
}

const vacio: Catalogos = { responsables: [], medios: [], tipos: [], estados: [], prioridades: [], areas: [] };
const ContextoCatalogos = createContext<ValorCatalogos>({ ...vacio, listo: false, recargar: () => {} });

export function ProveedorCatalogos({ children }: { children: ReactNode }) {
  const [catalogos, setCatalogos] = useState<Catalogos>(vacio);
  const [listo, setListo] = useState(false);

  const recargar = useCallback(() => {
    api
      .get<Catalogos>('/api/catalogos')
      .then((c) => {
        setCatalogos(c);
        setListo(true);
      })
      .catch(() => {});
  }, []);

  useEffect(recargar, [recargar]);
  useEvento('catalogos', recargar);

  const valor = useMemo(() => ({ ...catalogos, listo, recargar }), [catalogos, listo, recargar]);
  return <ContextoCatalogos.Provider value={valor}>{children}</ContextoCatalogos.Provider>;
}

export const useCatalogos = () => useContext(ContextoCatalogos);

export const activos = (lista: ItemCatalogo[]) => lista.filter((i) => i.activo);
export const predeterminado = (lista: ItemCatalogo[]) => lista.find((i) => i.activo && i.predeterminado) ?? null;

// ================================================================ contadores (menú)

const ContextoContadores = createContext<{ contadores: Contadores | null; recargar: () => void }>({
  contadores: null,
  recargar: () => {},
});

export function ProveedorContadores({ children }: { children: ReactNode }) {
  const [contadores, setContadores] = useState<Contadores | null>(null);
  const recargar = useCallback(() => {
    api.get<Contadores>('/api/solicitudes/contadores').then(setContadores).catch(() => {});
  }, []);
  useEffect(() => {
    recargar();
    const intervalo = setInterval(recargar, 60_000);
    return () => clearInterval(intervalo);
  }, [recargar]);
  useEvento('solicitudes', recargar);
  const valor = useMemo(() => ({ contadores, recargar }), [contadores, recargar]);
  return <ContextoContadores.Provider value={valor}>{children}</ContextoContadores.Provider>;
}

export const useContadores = () => useContext(ContextoContadores);

// ================================================================ avisos (toasts)

export interface Aviso {
  id: number;
  texto: string;
  tipo: 'ok' | 'error' | 'info';
  accion?: { texto: string; ejecutar: () => void };
}

const ContextoAvisos = createContext<(texto: string, tipo?: Aviso['tipo'], accion?: Aviso['accion']) => void>(() => {});

export function ProveedorAvisos({ children }: { children: ReactNode }) {
  const [avisos, setAvisos] = useState<Aviso[]>([]);
  const siguiente = useRef(1);

  const avisar = useCallback((texto: string, tipo: Aviso['tipo'] = 'ok', accion?: Aviso['accion']) => {
    const id = siguiente.current++;
    setAvisos((a) => [...a.slice(-3), { id, texto, tipo, accion }]);
    setTimeout(() => setAvisos((a) => a.filter((x) => x.id !== id)), tipo === 'error' ? 8000 : 5000);
  }, []);

  return (
    <ContextoAvisos.Provider value={avisar}>
      {children}
      <div className="avisos" role="status" aria-live="polite">
        {avisos.map((a) => (
          <div key={a.id} className={`aviso aviso-${a.tipo}`}>
            <span>{a.texto}</span>
            {a.accion && (
              <button
                type="button"
                className="aviso-accion"
                onClick={() => {
                  a.accion!.ejecutar();
                  setAvisos((lista) => lista.filter((x) => x.id !== a.id));
                }}
              >
                {a.accion.texto}
              </button>
            )}
            <button type="button" className="aviso-cerrar" aria-label="Cerrar aviso" onClick={() => setAvisos((lista) => lista.filter((x) => x.id !== a.id))}>
              ×
            </button>
          </div>
        ))}
      </div>
    </ContextoAvisos.Provider>
  );
}

export const useAvisar = () => useContext(ContextoAvisos);

/** Muestra el mensaje de un error de la API como aviso. */
export function mensajeError(e: unknown) {
  return e instanceof ErrorApi ? e.message : 'Ocurrió un error inesperado.';
}

// ================================================================ nueva solicitud (global)

const ContextoNueva = createContext<{ abrir: () => void }>({ abrir: () => {} });
export const ProveedorNueva = ContextoNueva.Provider;
export const useNuevaSolicitud = () => useContext(ContextoNueva);
