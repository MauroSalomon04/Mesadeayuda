// Formatos de fecha, duración y texto usados en toda la interfaz.
// Las fechas llegan del servidor como "2026-10-07T12:35:00" (hora local de Uruguay, sin zona).

const DIAS = ['Domingo', 'Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado'];
const MESES = ['enero', 'febrero', 'marzo', 'abril', 'mayo', 'junio', 'julio', 'agosto', 'septiembre', 'octubre', 'noviembre', 'diciembre'];
const MESES_CORTOS = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

/** Interpreta "2026-10-07T12:35:00" como fecha local (sin conversión de zona). */
export function aFecha(valor: string | null | undefined): Date | null {
  if (!valor) return null;
  const m = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?)?/.exec(valor);
  if (!m) return null;
  return new Date(+m[1], +m[2] - 1, +m[3], +(m[4] ?? 0), +(m[5] ?? 0), +(m[6] ?? 0));
}

const dos = (n: number) => String(n).padStart(2, '0');

/** 07/10/2026 */
export function fechaCorta(valor: string | Date | null | undefined) {
  const f = valor instanceof Date ? valor : aFecha(valor);
  return f ? `${dos(f.getDate())}/${dos(f.getMonth() + 1)}/${f.getFullYear()}` : '';
}

/** 12:35 */
export function hora(valor: string | Date | null | undefined) {
  const f = valor instanceof Date ? valor : aFecha(valor);
  return f ? `${dos(f.getHours())}:${dos(f.getMinutes())}` : '';
}

/** 07/10/2026 - 12:35 (formato del registro de la mesa) */
export function fechaHora(valor: string | Date | null | undefined) {
  const f = valor instanceof Date ? valor : aFecha(valor);
  return f ? `${fechaCorta(f)} - ${hora(f)}` : '';
}

/** Miércoles 07/10/2026 - 12:35 */
export function fechaHoraLarga(valor: string | Date | null | undefined) {
  const f = valor instanceof Date ? valor : aFecha(valor);
  return f ? `${DIAS[f.getDay()]} ${fechaHora(f)}` : '';
}

export function nombreDia(valor: string | Date | null | undefined) {
  const f = valor instanceof Date ? valor : aFecha(valor);
  return f ? DIAS[f.getDay()] : '';
}

/** "2026-10" → "octubre 2026" */
export function nombreMes(clave: string, corto = false) {
  const [anio, mes] = clave.split('-').map(Number);
  if (!anio || !mes) return clave;
  return corto ? `${MESES_CORTOS[mes - 1]} ${String(anio).slice(2)}` : `${MESES[mes - 1]} ${anio}`;
}

/** Fecha local en formato yyyy-MM-dd (para filtros). */
export function isoDia(fecha: Date) {
  return `${fecha.getFullYear()}-${dos(fecha.getMonth() + 1)}-${dos(fecha.getDate())}`;
}

/** Fecha y hora local en formato ISO sin zona (para enviar al servidor). */
export function isoLocal(fecha: Date) {
  return `${isoDia(fecha)}T${dos(fecha.getHours())}:${dos(fecha.getMinutes())}:00`;
}

/** 95 → "1 h 35 min" */
export function duracion(minutos: number | null | undefined) {
  if (minutos === null || minutos === undefined) return '';
  const m = Math.max(0, Math.round(minutos));
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  const resto = m % 60;
  if (h < 24) return resto ? `${h} h ${resto} min` : `${h} h`;
  const d = Math.floor(h / 24);
  const hr = h % 24;
  return hr ? `${d} d ${hr} h` : `${d} d`;
}

/** Tiempo transcurrido desde una fecha, en lenguaje simple: "hace 3 h", "2 días". */
export function tiempoDesde(valor: string | null | undefined, ahora = new Date()) {
  const f = aFecha(valor);
  if (!f) return '';
  const minutos = Math.max(0, Math.round((ahora.getTime() - f.getTime()) / 60000));
  if (minutos < 1) return 'menos de 1 min';
  if (minutos < 60) return `${minutos} min`;
  const horas = Math.floor(minutos / 60);
  if (horas < 24) return `${horas} h ${minutos % 60 ? `${minutos % 60} min` : ''}`.trim();
  const dias = Math.floor(horas / 24);
  if (dias < 31) return dias === 1 ? '1 día' : `${dias} días`;
  const meses = Math.floor(dias / 30.4);
  if (meses < 12) return meses === 1 ? '1 mes' : `${meses} meses`;
  const anios = Math.floor(meses / 12);
  return anios === 1 ? '1 año' : `${anios} años`;
}

/** Minutos transcurridos desde una fecha. */
export function minutosDesde(valor: string | null | undefined, ahora = new Date()) {
  const f = aFecha(valor);
  return f ? Math.max(0, (ahora.getTime() - f.getTime()) / 60000) : 0;
}

const formatoNumero = new Intl.NumberFormat('es-UY');
export const numero = (n: number | null | undefined) => (n === null || n === undefined ? '' : formatoNumero.format(n));

/** Quita tildes y pasa a minúsculas (igual que la búsqueda del servidor). */
export function normalizar(texto: string | null | undefined) {
  return (texto ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

export function plural(n: number, singular: string, pluralTexto?: string) {
  return `${numero(n)} ${n === 1 ? singular : pluralTexto ?? `${singular}s`}`;
}
