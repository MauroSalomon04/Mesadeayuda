import { useCallback } from 'react';
import { api } from '../api';
import type { SolicitudDetalle, SolicitudEntrada, SolicitudFila } from '../tipos';
import { emitir } from '../util/hooks';
import { mensajeError, useAvisar } from './contextos';

/**
 * Acciones rápidas sobre una solicitud (desde la tabla, pendientes o el detalle).
 * Cada una muestra un aviso con "Deshacer".
 */
export function useAccionesSolicitud() {
  const avisar = useAvisar();

  const cambiar = useCallback(
    async (fila: Pick<SolicitudFila, 'id' | 'numero'>, cambios: Partial<SolicitudEntrada>, mensaje: string, deshacer?: Partial<SolicitudEntrada>) => {
      const { id } = fila;
      try {
        const resultado = await api.patch<SolicitudDetalle>(`/api/solicitudes/${id}`, { cambios });
        emitir('solicitudes');
        avisar(
          mensaje,
          'ok',
          deshacer
            ? {
                texto: 'Deshacer',
                ejecutar: () => {
                  api
                    .patch(`/api/solicitudes/${id}`, { cambios: deshacer })
                    .then(() => {
                      emitir('solicitudes');
                      avisar(`Se deshizo el cambio en la solicitud #${fila.numero}.`, 'info');
                    })
                    .catch((e) => avisar(mensajeError(e), 'error'));
                },
              }
            : undefined,
        );
        return resultado;
      } catch (e) {
        avisar(mensajeError(e), 'error');
        return null;
      }
    },
    [avisar],
  );

  const resolver = useCallback(
    async (fila: Pick<SolicitudFila, 'id' | 'numero' | 'estadoId'>, extra?: { observaciones?: string; duracionEstimadaMin?: number | null }) => {
      try {
        const resultado = await api.post<SolicitudDetalle>(`/api/solicitudes/${fila.id}/resolver`, extra ?? {});
        emitir('solicitudes');
        if (fila.estadoId === null) {
          avisar(`Solicitud #${fila.numero} resuelta.`);
          return resultado;
        }
        avisar(`Solicitud #${fila.numero} resuelta.`, 'ok', {
          texto: 'Deshacer',
          ejecutar: () => {
            api
              .patch(`/api/solicitudes/${fila.id}`, { cambios: { estadoId: fila.estadoId } })
              .then(() => {
                emitir('solicitudes');
                avisar(`La solicitud #${fila.numero} volvió a quedar pendiente.`, 'info');
              })
              .catch((e) => avisar(mensajeError(e), 'error'));
          },
        });
        return resultado;
      } catch (e) {
        avisar(mensajeError(e), 'error');
        return null;
      }
    },
    [avisar],
  );

  return { cambiar, resolver };
}
