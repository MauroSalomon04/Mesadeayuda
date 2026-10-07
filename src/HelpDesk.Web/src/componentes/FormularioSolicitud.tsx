import { useState } from 'react';
import { LuCopy, LuExternalLink, LuLightbulb } from 'react-icons/lu';
import { api, query } from '../api';
import { activos, useCatalogos } from '../estado/contextos';
import type { ItemCatalogo, ResultadoSimilares, SolicitudEntrada, SolicitudFila, SugerenciaFuncionario, SugerenciaTexto } from '../tipos';
import { fechaCorta, numero, plural } from '../util/formato';
import { useCarga, useDebounce } from '../util/hooks';
import { Autocompletar, Estado, Resaltado, Segmentado } from './basicos';

/** Estado del formulario (los campos de texto se manejan como texto). */
export interface FormSolicitud {
  nombreFuncionario: string;
  oficina: string;
  medioContactoId: number | null;
  tipoSolicitudId: number | null;
  descripcion: string;
  observaciones: string;
  responsableId: number | null;
  duracionEstimadaMin: string;
  estadoId: number | null;
  prioridadId: number | null;
}

export type ErroresForm = Partial<Record<keyof FormSolicitud, string>>;

export function formDesdeSolicitud(s: SolicitudFila): FormSolicitud {
  return {
    nombreFuncionario: s.nombreFuncionario ?? '',
    oficina: s.oficina ?? '',
    medioContactoId: s.medioContactoId,
    tipoSolicitudId: s.tipoSolicitudId,
    descripcion: s.descripcion ?? '',
    observaciones: s.observaciones ?? '',
    responsableId: s.responsableId,
    duracionEstimadaMin: s.duracionEstimadaMin !== null ? String(s.duracionEstimadaMin) : '',
    estadoId: s.estadoId,
    prioridadId: s.prioridadId,
  };
}

export function entradaDesdeForm(f: FormSolicitud): SolicitudEntrada {
  const texto = (v: string) => (v.trim() === '' ? null : v.trim());
  const duracion = f.duracionEstimadaMin.trim() === '' ? null : Number(f.duracionEstimadaMin);
  return {
    nombreFuncionario: texto(f.nombreFuncionario),
    oficina: texto(f.oficina),
    medioContactoId: f.medioContactoId,
    tipoSolicitudId: f.tipoSolicitudId,
    descripcion: texto(f.descripcion),
    observaciones: f.observaciones.trim() === '' ? null : f.observaciones.replace(/\s+$/, ''),
    responsableId: f.responsableId,
    duracionEstimadaMin: duracion !== null && Number.isFinite(duracion) ? Math.round(duracion) : null,
    estadoId: f.estadoId,
    prioridadId: f.prioridadId,
  };
}

export function validarForm(f: FormSolicitud): ErroresForm {
  const errores: ErroresForm = {};
  if (!f.descripcion.trim()) errores.descripcion = 'Escribí una descripción breve del problema.';
  if (f.medioContactoId === null) errores.medioContactoId = 'Elegí el medio de contacto.';
  if (f.responsableId === null) errores.responsableId = 'Elegí el responsable.';
  if (f.estadoId === null) errores.estadoId = 'Elegí el estado.';
  if (f.prioridadId === null) errores.prioridadId = 'Elegí la prioridad.';
  const d = f.duracionEstimadaMin.trim();
  if (d && (!/^\d+$/.test(d) || Number(d) > 100000)) errores.duracionEstimadaMin = 'Ingresá los minutos como número entero.';
  return errores;
}

/** Opciones activas + el valor actual aunque esté desactivado (para no perderlo al editar). */
function opciones(lista: ItemCatalogo[], actual: number | null) {
  const activas = activos(lista);
  if (actual !== null && !activas.some((o) => o.id === actual)) {
    const existente = lista.find((o) => o.id === actual);
    if (existente) return [...activas, existente];
  }
  return activas;
}

const DURACIONES = [5, 10, 15, 20, 30, 60];

export function CamposSolicitud({
  form,
  cambiar,
  errores,
  enfocarFuncionario,
}: {
  form: FormSolicitud;
  cambiar: <K extends keyof FormSolicitud>(campo: K, valor: FormSolicitud[K]) => void;
  errores: ErroresForm;
  enfocarFuncionario?: boolean;
}) {
  const catalogos = useCatalogos();
  const error = (campo: keyof FormSolicitud) =>
    errores[campo] ? (
      <span className="error-campo" role="alert">
        {errores[campo]}
      </span>
    ) : null;
  const clase = (campo: keyof FormSolicitud, extra = '') => `campo${errores[campo] ? ' con-error' : ''}${extra}`;

  return (
    <>
      <div className={clase('nombreFuncionario')}>
        <label htmlFor="f-funcionario">Nombre del funcionario</label>
        <Autocompletar
          id="f-funcionario"
          valor={form.nombreFuncionario}
          onChange={(v) => cambiar('nombreFuncionario', v)}
          autoFocus={enfocarFuncionario}
          maxLength={150}
          placeholder="Quién llama o escribe"
          minimo={2}
          buscar={(t) =>
            api.get<SugerenciaFuncionario[]>(`/api/sugerencias/funcionarios${query({ q: t })}`).then((lista) =>
              lista.map((s) => ({
                clave: `${s.nombre}|${s.oficina ?? ''}`,
                texto: s.nombre,
                extra: s.oficina ?? undefined,
                dato: s,
              })),
            )
          }
          onElegir={(s) => {
            const oficina = (s.dato as SugerenciaFuncionario | undefined)?.oficina;
            if (oficina) cambiar('oficina', oficina);
          }}
        />
        {error('nombreFuncionario')}
      </div>

      <div className={clase('oficina')}>
        <label htmlFor="f-oficina-sol">Oficina</label>
        <Autocompletar
          id="f-oficina-sol"
          valor={form.oficina}
          onChange={(v) => cambiar('oficina', v)}
          maxLength={150}
          placeholder="Se sugieren las oficinas ya usadas"
          buscar={(t) =>
            api
              .get<SugerenciaTexto[]>(`/api/sugerencias/oficinas${query({ q: t })}`)
              .then((lista) => lista.map((s) => ({ clave: s.texto, texto: s.texto, extra: plural(s.cantidad, 'solicitud', 'solicitudes') })))
          }
        />
        {error('oficina')}
      </div>

      <div className={clase('medioContactoId')}>
        <span className="campo-etiqueta">Medio de contacto</span>
        <Segmentado etiqueta="Medio de contacto" opciones={opciones(catalogos.medios, form.medioContactoId)} valor={form.medioContactoId} onChange={(v) => cambiar('medioContactoId', v)} />
        {error('medioContactoId')}
      </div>

      <div className={clase('tipoSolicitudId')}>
        <span className="campo-etiqueta">Tipo de solicitud</span>
        <Segmentado
          etiqueta="Tipo de solicitud"
          codigo
          permitirVacio
          opciones={opciones(catalogos.tipos, form.tipoSolicitudId)}
          valor={form.tipoSolicitudId}
          onChange={(v) => cambiar('tipoSolicitudId', v)}
        />
        {error('tipoSolicitudId')}
      </div>

      <div className={clase('descripcion', ' ancho')}>
        <label htmlFor="f-descripcion">Descripción de la solicitud</label>
        <Autocompletar
          id="f-descripcion"
          valor={form.descripcion}
          onChange={(v) => cambiar('descripcion', v)}
          maxLength={500}
          minimo={2}
          invalido={!!errores.descripcion}
          placeholder="Ej.: Error Etoken, Creación usuario, Reseteo contraseña"
          buscar={(t) =>
            api
              .get<SugerenciaTexto[]>(`/api/sugerencias/descripciones${query({ q: t })}`)
              .then((lista) => lista.map((s) => ({ clave: s.texto, texto: s.texto, extra: `${numero(s.cantidad)} veces` })))
          }
        />
        {error('descripcion')}
      </div>

      <div className={clase('observaciones', ' ancho')}>
        <label htmlFor="f-observaciones">Observaciones</label>
        <textarea
          id="f-observaciones"
          className="area-texto"
          value={form.observaciones}
          onChange={(e) => cambiar('observaciones', e.target.value)}
          placeholder="Qué se hizo para resolver o analizar el problema"
          rows={3}
        />
        {error('observaciones')}
      </div>

      <div className={clase('responsableId')}>
        <span className="campo-etiqueta">Responsable</span>
        <Segmentado etiqueta="Responsable" opciones={opciones(catalogos.responsables, form.responsableId)} valor={form.responsableId} onChange={(v) => cambiar('responsableId', v)} />
        {error('responsableId')}
      </div>

      <div className={clase('duracionEstimadaMin')}>
        <label htmlFor="f-duracion">Duración estimada (min)</label>
        <div className="duraciones">
          <input
            id="f-duracion"
            className="entrada num"
            inputMode="numeric"
            value={form.duracionEstimadaMin}
            onChange={(e) => cambiar('duracionEstimadaMin', e.target.value.replace(/[^\d]/g, ''))}
            placeholder="min"
          />
          {DURACIONES.map((d) => (
            <button
              key={d}
              type="button"
              className={`seg-opcion${form.duracionEstimadaMin === String(d) ? ' marcada' : ''}`}
              onClick={() => cambiar('duracionEstimadaMin', String(d))}
            >
              {d}
            </button>
          ))}
        </div>
        {error('duracionEstimadaMin')}
      </div>

      <div className={clase('estadoId')}>
        <span className="campo-etiqueta">Estado</span>
        <Segmentado etiqueta="Estado" opciones={opciones(catalogos.estados, form.estadoId)} valor={form.estadoId} onChange={(v) => cambiar('estadoId', v)} />
        {error('estadoId')}
      </div>

      <div className={clase('prioridadId')}>
        <span className="campo-etiqueta">Prioridad</span>
        <Segmentado etiqueta="Prioridad" opciones={opciones(catalogos.prioridades, form.prioridadId)} valor={form.prioridadId} onChange={(v) => cambiar('prioridadId', v)} />
        {error('prioridadId')}
      </div>
    </>
  );
}

/** Panel de casos anteriores parecidos (base de conocimiento sin IA). */
export function CasosSimilares({
  texto,
  excluirId,
  onUsarSolucion,
  onAbrir,
}: {
  texto: string;
  excluirId?: number;
  onUsarSolucion?: (solucion: string, id: number) => void;
  onAbrir?: (id: number) => void;
}) {
  const diferido = useDebounce(texto.trim(), 350);
  const [cantidad, setCantidad] = useState(8);
  const { datos, cargando } = useCarga(
    diferido.length >= 3 ? () => api.get<ResultadoSimilares>(`/api/conocimiento/similares${query({ texto: diferido, excluir: excluirId, limite: 30 })}`) : null,
    [diferido, excluirId],
  );

  if (diferido.length < 3) {
    return (
      <div>
        <div className="conocimiento-titulo">
          <LuLightbulb aria-hidden style={{ verticalAlign: -2, marginRight: 6, color: 'var(--pen)' }} />
          Casos anteriores
        </div>
        <p className="conocimiento-resumen">Al escribir la descripción aparecen las solicitudes parecidas y cómo se resolvieron.</p>
      </div>
    );
  }

  const resultado = datos && diferido.length >= 3 ? datos : null;
  return (
    <div aria-live="polite">
      <div className="conocimiento-titulo">
        <LuLightbulb aria-hidden style={{ verticalAlign: -2, marginRight: 6, color: 'var(--pen)' }} />
        Casos anteriores
      </div>
      {!resultado && cargando && <p className="conocimiento-resumen">Buscando…</p>}
      {resultado && resultado.total === 0 && <p className="conocimiento-resumen">No se encontraron casos parecidos.</p>}
      {resultado && resultado.total > 0 && (
        <>
          <p className="conocimiento-resumen">
            Se encontraron <strong>{numero(resultado.total)}</strong> {resultado.total === 1 ? 'caso similar' : 'casos similares'}
            {resultado.conSolucion > 0 && `, ${numero(resultado.conSolucion)} con la solución anotada`}.
          </p>
          {resultado.casos.slice(0, cantidad).map((c) => (
            <article key={c.id} className="caso">
              <div className="caso-cabecera">
                <span className="num">#{c.id}</span>
                <span>{fechaCorta(c.fechaIngreso)}</span>
                <span>{c.responsable}</span>
                <span style={{ marginLeft: 'auto' }}>
                  <Estado nombre={c.estado} esResuelto={c.estadoEsResuelto} />
                </span>
              </div>
              <div className="caso-descripcion">
                <Resaltado texto={c.descripcion} palabras={resultado.palabras} />
              </div>
              {c.observaciones ? (
                <div className="caso-solucion">
                  <Resaltado texto={c.observaciones} palabras={resultado.palabras} />
                </div>
              ) : (
                <div className="caso-sin-solucion">Sin observaciones anotadas.</div>
              )}
              {(onUsarSolucion || onAbrir) && (
                <div className="caso-acciones">
                  {onUsarSolucion && c.observaciones && (
                    <button type="button" className="btn btn-chico btn-sutil" onClick={() => onUsarSolucion(c.observaciones!, c.id)}>
                      <LuCopy /> Copiar a observaciones
                    </button>
                  )}
                  {onAbrir && (
                    <button type="button" className="btn btn-chico btn-sutil" onClick={() => onAbrir(c.id)}>
                      <LuExternalLink /> Abrir
                    </button>
                  )}
                </div>
              )}
            </article>
          ))}
          {resultado.casos.length > cantidad && (
            <button type="button" className="btn btn-chico" onClick={() => setCantidad((n) => n + 10)}>
              Ver más casos
            </button>
          )}
        </>
      )}
    </div>
  );
}
