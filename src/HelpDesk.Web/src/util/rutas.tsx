import { AnchorHTMLAttributes, MouseEvent, useSyncExternalStore } from 'react';

// Enrutador mínimo basado en la History API (sin dependencias).

const EVENTO = 'helpdesk:navegacion';

function suscribir(aviso: () => void) {
  window.addEventListener('popstate', aviso);
  window.addEventListener(EVENTO, aviso);
  return () => {
    window.removeEventListener('popstate', aviso);
    window.removeEventListener(EVENTO, aviso);
  };
}

const instantanea = () => window.location.pathname + window.location.search;

export function useUbicacion() {
  const actual = useSyncExternalStore(suscribir, instantanea);
  const url = new URL(actual, window.location.origin);
  return { ruta: url.pathname, params: url.searchParams };
}

export function navegar(destino: string, reemplazar = false) {
  if (destino === instantanea()) return;
  if (reemplazar) window.history.replaceState(null, '', destino);
  else window.history.pushState(null, '', destino);
  window.dispatchEvent(new Event(EVENTO));
}

/** Cambia parámetros de la URL actual (null los quita). */
export function cambiarParams(cambios: Record<string, string | number | null | undefined>, reemplazar = false) {
  const url = new URL(window.location.href);
  for (const [clave, valor] of Object.entries(cambios)) {
    if (valor === null || valor === undefined || valor === '') url.searchParams.delete(clave);
    else url.searchParams.set(clave, String(valor));
  }
  navegar(url.pathname + url.search, reemplazar);
}

export function Enlace({ a, onClick, ...resto }: AnchorHTMLAttributes<HTMLAnchorElement> & { a: string }) {
  const alHacerClic = (e: MouseEvent<HTMLAnchorElement>) => {
    onClick?.(e);
    if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    navegar(a);
  };
  return <a href={a} onClick={alHacerClic} {...resto} />;
}
