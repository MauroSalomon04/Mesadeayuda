import { useEffect } from 'react';
import { CLIENTE } from '../api';
import { emitir, type Evento } from '../util/hooks';

// Actualización en tiempo real: el servidor avisa por Server-Sent Events (/api/eventos) qué datos
// cambiaron y cada pantalla vuelve a pedir solo lo suyo, conservando filtros, búsqueda y página
// (las cargas existentes ya escuchan los eventos 'solicitudes', 'catalogos' y 'tareas').

const TODOS: Evento[] = ['solicitudes', 'catalogos', 'tareas'];
// Agrupa ráfagas de avisos (por ejemplo, una importación) en una sola recarga.
const DEMORA_MS = 250;
// Si el servidor rechaza la conexión (sesión vencida, reinicio), se reintenta con esta pausa.
const REINTENTO_MS = 10_000;

let conectado = false;

/** Indica si los avisos en tiempo real están llegando (para no repetir consultas periódicas). */
export const tiempoRealConectado = () => conectado;

interface Aviso {
  tipos?: string[];
  origen?: string | null;
}

export function useTiempoReal() {
  useEffect(() => {
    if (typeof EventSource === 'undefined') return;

    const pendientes = new Set<Evento>();
    let temporizador: number | undefined;
    let reintento: number | undefined;
    let fuente: EventSource | null = null;
    let primeraConexion = true;

    const despachar = () => {
      temporizador = undefined;
      // Con la pestaña oculta no se consulta al servidor: se actualiza al volver.
      if (document.hidden) return;
      const tipos = [...pendientes];
      pendientes.clear();
      tipos.forEach(emitir);
    };

    const programar = (tipos: Evento[]) => {
      if (tipos.length === 0) return;
      tipos.forEach((t) => pendientes.add(t));
      if (temporizador === undefined) temporizador = window.setTimeout(despachar, DEMORA_MS);
    };

    const alCambiarVisibilidad = () => {
      if (!document.hidden && pendientes.size > 0 && temporizador === undefined) despachar();
    };

    const conectar = () => {
      fuente = new EventSource('/api/eventos');
      fuente.onopen = () => {
        conectado = true;
        // Una conexión nueva (no una reconexión automática con Last-Event-ID) no sabe qué se perdió.
        if (!primeraConexion) programar(TODOS);
        primeraConexion = false;
      };
      fuente.onmessage = (e: MessageEvent<string>) => {
        let aviso: Aviso;
        try {
          aviso = JSON.parse(e.data) as Aviso;
        } catch {
          return;
        }
        if (aviso.origen && aviso.origen === CLIENTE) return; // esta pestaña ya se actualizó
        const tipos = (aviso.tipos ?? []).flatMap((t) => (t === 'todo' ? TODOS : TODOS.includes(t as Evento) ? [t as Evento] : []));
        programar(tipos);
      };
      // El servidor no puede recuperar los avisos perdidos (por ejemplo, se reinició): se recarga todo.
      fuente.addEventListener('resincronizar', () => programar(TODOS));
      fuente.onerror = () => {
        conectado = false;
        // Si la conexión se cortó, EventSource se reconecta solo. Si el servidor respondió con un
        // error queda cerrada: se vuelve a intentar más tarde.
        if (fuente?.readyState === EventSource.CLOSED) {
          fuente.close();
          reintento = window.setTimeout(conectar, REINTENTO_MS);
        }
      };
    };

    conectar();
    document.addEventListener('visibilitychange', alCambiarVisibilidad);
    return () => {
      document.removeEventListener('visibilitychange', alCambiarVisibilidad);
      window.clearTimeout(temporizador);
      window.clearTimeout(reintento);
      fuente?.close();
      conectado = false;
    };
  }, []);
}
