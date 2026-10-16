import { useCallback, useEffect, useRef, useState } from 'react';
import { ErrorApi } from '../api';

// ---------------------------------------------------------------- eventos
// Bus simple para avisar a las pantallas que deben recargar datos.
export type Evento = 'solicitudes' | 'catalogos' | 'tareas';
const oyentes = new Map<Evento, Set<() => void>>();

export function emitir(evento: Evento) {
  oyentes.get(evento)?.forEach((f) => f());
}

export function useEvento(evento: Evento, funcion: () => void) {
  const ref = useRef(funcion);
  ref.current = funcion;
  useEffect(() => {
    const envoltura = () => ref.current();
    if (!oyentes.has(evento)) oyentes.set(evento, new Set());
    oyentes.get(evento)!.add(envoltura);
    return () => {
      oyentes.get(evento)?.delete(envoltura);
    };
  }, [evento]);
}

// ---------------------------------------------------------------- datos
export interface EstadoCarga<T> {
  datos: T | undefined;
  error: ErrorApi | null;
  cargando: boolean;
  recargar: () => void;
  setDatos: (d: T) => void;
}

/**
 * Carga datos de la API y los vuelve a pedir cuando cambian las dependencias.
 * Conserva los datos anteriores mientras carga (la tabla no "parpadea").
 */
export function useCarga<T>(cargar: (() => Promise<T>) | null, dependencias: unknown[], recargarCon?: Evento): EstadoCarga<T> {
  const [datos, setDatos] = useState<T>();
  const [error, setError] = useState<ErrorApi | null>(null);
  const [cargando, setCargando] = useState(false);
  const [version, setVersion] = useState(0);
  const recargar = useCallback(() => setVersion((v) => v + 1), []);

  useEvento(recargarCon ?? ('__ninguno__' as Evento), recargar);

  useEffect(() => {
    if (!cargar) return;
    let vigente = true;
    setCargando(true);
    cargar()
      .then((d) => {
        if (!vigente) return;
        setDatos(d);
        setError(null);
      })
      .catch((e: unknown) => {
        if (!vigente) return;
        setError(e instanceof ErrorApi ? e : new ErrorApi(0, String(e)));
      })
      .finally(() => {
        if (vigente) setCargando(false);
      });
    return () => {
      vigente = false;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...dependencias, version]);

  return { datos, error, cargando, recargar, setDatos };
}

export function useDebounce<T>(valor: T, ms = 250): T {
  const [diferido, setDiferido] = useState(valor);
  useEffect(() => {
    const t = setTimeout(() => setDiferido(valor), ms);
    return () => clearTimeout(t);
  }, [valor, ms]);
  return diferido;
}

/** Hora actual que se actualiza cada `ms` (para relojes y "tiempo pendiente"). */
export function useAhora(ms = 30000) {
  const [ahora, setAhora] = useState(() => new Date());
  useEffect(() => {
    const t = setInterval(() => setAhora(new Date()), ms);
    return () => clearInterval(t);
  }, [ms]);
  return ahora;
}

/** Atajo de teclado global (ignorado mientras se escribe en un campo). */
export function useAtajo(tecla: string, accion: (e: KeyboardEvent) => void, activo = true) {
  const ref = useRef(accion);
  ref.current = accion;
  useEffect(() => {
    if (!activo) return;
    const manejador = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const destino = e.target as HTMLElement | null;
      if (destino && (destino.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(destino.tagName))) return;
      if (document.querySelector('[role="dialog"]')) return;
      if (e.key.toLowerCase() === tecla.toLowerCase()) {
        e.preventDefault();
        ref.current(e);
      }
    };
    window.addEventListener('keydown', manejador);
    return () => window.removeEventListener('keydown', manejador);
  }, [tecla, activo]);
}
