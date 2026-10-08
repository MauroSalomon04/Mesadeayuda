import { Dialog, DialogBackdrop, DialogPanel, DialogTitle } from '@headlessui/react';
import { FormEvent, KeyboardEvent, useEffect, useRef, useState } from 'react';
import { LuX } from 'react-icons/lu';
import { api } from '../api';
import { Confirmar } from '../componentes/basicos';
import { CamposSolicitud, CasosSimilares, entradaDesdeForm, ErroresForm, FormSolicitud, validarForm } from '../componentes/FormularioSolicitud';
import { mensajeError, predeterminado, useAvisar, useCatalogos, useUsuario } from '../estado/contextos';
import type { SolicitudDetalle } from '../tipos';
import { fechaCorta, hora, nombreDia } from '../util/formato';
import { emitir, useAhora } from '../util/hooks';
import { cambiarParams } from '../util/rutas';

export function NuevaSolicitud({ abierto, onCerrar }: { abierto: boolean; onCerrar: () => void }) {
  const usuario = useUsuario();
  const catalogos = useCatalogos();
  const avisar = useAvisar();
  const ahora = useAhora(10_000);

  const formInicial = (): FormSolicitud => {
    const responsable = catalogos.responsables.find((r) => r.id === usuario.responsableId && r.activo);
    return {
      nombreFuncionario: '',
      oficina: '',
      medioContactoId: predeterminado(catalogos.medios)?.id ?? null,
      tipoSolicitudId: null,
      descripcion: '',
      observaciones: '',
      responsableId: responsable?.id ?? null,
      duracionEstimadaMin: '',
      estadoId: predeterminado(catalogos.estados)?.id ?? null,
      prioridadId: predeterminado(catalogos.prioridades)?.id ?? null,
    };
  };

  const [form, setForm] = useState<FormSolicitud>(formInicial);
  const [errores, setErrores] = useState<ErroresForm>({});
  const [guardando, setGuardando] = useState(false);
  const [errorGeneral, setErrorGeneral] = useState<string | null>(null);
  const [proximoId, setProximoId] = useState<number | null>(null);
  const [confirmarDescarte, setConfirmarDescarte] = useState(false);
  const [contador, setContador] = useState(0); // fuerza el foco al reiniciar
  const inicial = useRef<FormSolicitud>(form);

  // Al abrir: formulario limpio con los valores predeterminados y el próximo número.
  useEffect(() => {
    if (!abierto) return;
    const limpio = formInicial();
    inicial.current = limpio;
    setForm(limpio);
    setErrores({});
    setErrorGeneral(null);
    api
      .get<{ id: number }>('/api/solicitudes/proximo-id')
      .then((r) => setProximoId(r.id))
      .catch(() => setProximoId(null));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [abierto, contador, catalogos.listo]);

  const cambiar = <K extends keyof FormSolicitud>(campo: K, valor: FormSolicitud[K]) => {
    setForm((f) => ({ ...f, [campo]: valor }));
    if (errores[campo]) setErrores((e) => ({ ...e, [campo]: undefined }));
  };

  const modificado = JSON.stringify(form) !== JSON.stringify(inicial.current);

  const guardar = async (otra: boolean) => {
    const encontrados = validarForm(form);
    setErrores(encontrados);
    if (Object.keys(encontrados).length > 0) return;
    setGuardando(true);
    setErrorGeneral(null);
    try {
      const creada = await api.post<SolicitudDetalle>('/api/solicitudes', entradaDesdeForm(form));
      emitir('solicitudes');
      avisar(`Solicitud #${creada.numero} registrada${creada.estadoEsResuelto ? ' como resuelta' : ` como ${creada.estado}`}.`, 'ok', {
        texto: 'Ver',
        ejecutar: () => cambiarParams({ ver: creada.id }),
      });
      if (otra) setContador((c) => c + 1);
      else onCerrar();
    } catch (e) {
      setErrorGeneral(mensajeError(e));
    } finally {
      setGuardando(false);
    }
  };

  const enviar = (e: FormEvent) => {
    e.preventDefault();
    void guardar(false);
  };

  const alPresionar = (e: KeyboardEvent) => {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      void guardar(e.shiftKey);
    }
  };

  const intentarCerrar = () => {
    if (modificado && !guardando) setConfirmarDescarte(true);
    else onCerrar();
  };

  const usarSolucion = (solucion: string, numero: number) => {
    const agregado = `${solucion}\n(Según la solicitud #${numero})`;
    cambiar('observaciones', form.observaciones.trim() ? `${form.observaciones.trim()}\n${agregado}` : agregado);
  };

  return (
    <>
      <Dialog open={abierto} onClose={intentarCerrar}>
        <DialogBackdrop className="velo" />
        <div className="dialogo-capa">
          <DialogPanel className="dialogo boleta">
            <form onSubmit={enviar} onKeyDown={alPresionar} noValidate>
              <div className="boleta-cabecera">
                <div className="sello">
                  <span className="sello-etiqueta">Nueva solicitud</span>
                  <DialogTitle as="span" className="sello-numero">
                    N.º {proximoId ?? '…'}
                  </DialogTitle>
                </div>
                <div className="boleta-fecha">
                  <strong>{nombreDia(ahora)}</strong> {fechaCorta(ahora)} - {hora(ahora)}
                  <div className="tenue" style={{ fontSize: 'var(--t-12)' }}>
                    El número y la hora se asignan al guardar. Registra: {usuario.nombre}.
                  </div>
                </div>
                <button type="button" className="btn btn-sutil btn-icono" onClick={intentarCerrar} aria-label="Cerrar">
                  <LuX />
                </button>
              </div>

              <div className="boleta-cuerpo">
                <div className="boleta-formulario">
                  <CamposSolicitud key={contador} form={form} cambiar={cambiar} errores={errores} enfocarFuncionario />
                  {errorGeneral && (
                    <div className="error-caja ancho" role="alert">
                      {errorGeneral}
                    </div>
                  )}
                </div>
                <aside className="boleta-conocimiento" aria-label="Casos anteriores">
                  <CasosSimilares texto={form.descripcion} onUsarSolucion={usarSolucion} />
                </aside>
              </div>

              <div className="dialogo-pie">
                <span className="tenue" style={{ marginRight: 'auto', fontSize: 'var(--t-12)', alignSelf: 'center' }}>
                  <span className="tecla">Ctrl</span> + <span className="tecla">Enter</span> guarda. Con <span className="tecla">Shift</span> registra otra.
                </span>
                <button type="button" className="btn btn-sutil" onClick={intentarCerrar}>
                  Cancelar
                </button>
                <button type="button" className="btn" disabled={guardando} onClick={() => void guardar(true)}>
                  Guardar y registrar otra
                </button>
                <button type="submit" className="btn btn-primario" disabled={guardando}>
                  {guardando ? 'Guardando…' : 'Guardar solicitud'}
                </button>
              </div>
            </form>
            <Confirmar
              abierto={confirmarDescarte}
              titulo="¿Descartar la solicitud?"
              texto="Los datos que escribiste no se guardaron."
              accion="Descartar"
              peligro
              onCancelar={() => setConfirmarDescarte(false)}
              onConfirmar={() => {
                setConfirmarDescarte(false);
                onCerrar();
              }}
            />
          </DialogPanel>
        </div>
      </Dialog>
    </>
  );
}
