// Tipos que devuelve la API (mismos nombres que los DTO de C#, en camelCase).

export type Rol = 'ADMIN' | 'SOPORTE';

export interface Sesion {
  id: number;
  usuario: string;
  nombre: string;
  rol: Rol;
  responsableId: number | null;
  debeCambiarPassword: boolean;
}

export interface ItemCatalogo {
  id: number;
  nombre: string;
  activo: boolean;
  orden: number;
  predeterminado: boolean;
  esResuelto: boolean | null;
  color: string | null;
  nivel: number | null;
  descripcion: string | null;
  usos: number | null;
}

export interface Catalogos {
  responsables: ItemCatalogo[];
  medios: ItemCatalogo[];
  tipos: ItemCatalogo[];
  estados: ItemCatalogo[];
  prioridades: ItemCatalogo[];
  areas: ItemCatalogo[];
}

export interface SolicitudFila {
  id: number;
  fechaIngreso: string;
  diaSemana: number;
  nombreFuncionario: string | null;
  oficinaId: number | null;
  oficina: string | null;
  medioContactoId: number | null;
  medioContacto: string | null;
  tipoSolicitudId: number | null;
  tipoSolicitud: string | null;
  descripcion: string | null;
  observaciones: string | null;
  responsableId: number | null;
  responsable: string | null;
  duracionEstimadaMin: number | null;
  estadoId: number | null;
  estado: string | null;
  estadoEsResuelto: boolean | null;
  estadoColor: string | null;
  prioridadId: number | null;
  prioridad: string | null;
  prioridadNivel: number | null;
  fechaResolucion: string | null;
  minutosResolucion: number | null;
  origen: 'APP' | 'IMPORTACION';
  version: number;
}

export interface HistorialItem {
  id: number;
  fechaHora: string;
  usuario: string | null;
  accion: string;
  campo: string | null;
  valorAnterior: string | null;
  valorNuevo: string | null;
  detalle: string | null;
}

export interface SolicitudDetalle extends SolicitudFila {
  creadoPor: string | null;
  creadoEn: string;
  actualizadoPor: string | null;
  actualizadoEn: string;
  filaExcel: number | null;
  importacionId: number | null;
  historial: HistorialItem[];
}

export interface Pagina<T> {
  items: T[];
  total: number;
  pagina: number;
  tamano: number;
}

export interface Contadores {
  pendientes: number;
  misPendientes: number;
  sinEstado: number;
  hoy: number;
}

/** Campos que se pueden enviar al crear o editar una solicitud. */
export interface SolicitudEntrada {
  nombreFuncionario: string | null;
  oficina: string | null;
  medioContactoId: number | null;
  tipoSolicitudId: number | null;
  descripcion: string | null;
  observaciones: string | null;
  responsableId: number | null;
  duracionEstimadaMin: number | null;
  estadoId: number | null;
  prioridadId: number | null;
}

export interface Conflicto {
  campo: keyof SolicitudEntrada;
  etiqueta: string;
  valorActual: string | null;
  actualizadoPor: string | null;
  actualizadoEn: string;
}

export interface CasoSimilar {
  id: number;
  fechaIngreso: string;
  nombreFuncionario: string | null;
  oficina: string | null;
  descripcion: string | null;
  observaciones: string | null;
  responsable: string | null;
  estado: string | null;
  estadoEsResuelto: boolean | null;
  tipoSolicitud: string | null;
  puntaje: number;
}

export interface ResultadoSimilares {
  total: number;
  conSolucion: number;
  casos: CasoSimilar[];
  palabras: string[];
}

export interface SugerenciaFuncionario {
  nombre: string;
  oficina: string | null;
  cantidad: number;
  ultimaFecha: string;
}

export interface SugerenciaTexto {
  texto: string;
  cantidad: number;
}

export interface Conteo {
  etiqueta: string;
  cantidad: number;
}

export interface Dashboard {
  periodo: string;
  desde: string | null;
  indicadores: { hoy: number; semana: number; mes: number; pendientes: number; sinEstado: number };
  enPeriodo: {
    total: number;
    resueltas: number;
    promedioResolucionMin: number | null;
    resueltasConTiempo: number;
    resueltasAlRegistrar: number;
    duracionPromedioMin: number | null;
    minutosEstimados: number;
  };
  porTipo: Conteo[];
  porResponsable: Conteo[];
  porOficina: Conteo[];
  porMedio: Conteo[];
  porPrioridad: Conteo[];
  porEstado: Conteo[];
  porMes: { mes: string; cantidad: number; pendientes: number }[];
  topProblemas: Conteo[];
}

export interface FilaReporte {
  clave: string;
  etiqueta: string;
  cantidad: number;
  minutos: number;
  promedioMin: number | null;
  celdasCantidad: number[];
  celdasMinutos: number[];
}

export interface Reporte {
  dimension: string;
  dimensionEtiqueta: string;
  dimension2: string | null;
  dimension2Etiqueta: string | null;
  columnas: string[];
  filas: FilaReporte[];
  totales: FilaReporte;
}

export interface TareaExtra {
  id: number;
  areaAsseId: number | null;
  area: string | null;
  tarea: string;
  impacto: string | null;
  cargaTrabajo: string | null;
  origen: string;
  creadoEn: string;
  creadoPor: string | null;
  actualizadoEn: string;
  actualizadoPor: string | null;
  implicados: { id: number; nombre: string; activo: boolean }[];
}

export interface UsuarioVista {
  id: number;
  usuario: string;
  nombre: string;
  rol: Rol;
  responsableId: number | null;
  responsable: string | null;
  activo: boolean;
  debeCambiarPassword: boolean;
  creadoEn: string;
  ultimoAcceso: string | null;
}

export interface OficinaItem {
  id: number;
  nombre: string;
  activo: boolean;
  solicitudes: number;
  ultimaSolicitud: string | null;
}

export interface Incidencia {
  hoja: string;
  fila: number;
  id: number | null;
  tipo: 'error' | 'advertencia';
  mensaje: string;
}

export interface Previsualizacion {
  token: string;
  nombreArchivo: string;
  hojaSolicitudes: string | null;
  hojaTareas: string | null;
  solicitudes: {
    filasLeidas: number;
    validas: number;
    nuevas: number;
    existentes: number;
    conError: number;
    conAdvertencia: number;
    idMinimo: number | null;
    idMaximo: number | null;
    pendientes: number;
  };
  tareasExtra: { leidas: number; nuevas: number; existentes: number };
  catalogosNuevos: {
    responsables: string[];
    responsablesInactivos: string[];
    medios: string[];
    tipos: string[];
    estados: string[];
    prioridades: string[];
    areas: string[];
    oficinas: number;
    oficinasEjemplo: string[];
  };
  incidencias: Incidencia[];
  totalIncidencias: number;
  muestra: {
    id: number;
    fechaIngreso: string;
    nombreFuncionario: string | null;
    oficina: string | null;
    descripcion: string | null;
    responsable: string | null;
    estado: string | null;
  }[];
}

export interface ResultadoImportacion {
  importacionId: number;
  solicitudesNuevas: number;
  solicitudesExistentes: number;
  filasConError: number;
  tareasExtraNuevas: number;
  proximoId: number;
}

export interface ImportacionHistorica {
  id: number;
  fechaHora: string;
  usuario: string | null;
  nombreArchivo: string;
  filasLeidas: number;
  solicitudesNuevas: number;
  solicitudesExistentes: number;
  filasConError: number;
  filasConAdvertencia: number;
  tareasExtraNuevas: number;
}
