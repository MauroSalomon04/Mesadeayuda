// Cliente HTTP de la API. Todas las rutas son relativas al mismo servidor.

export class ErrorApi extends Error {
  constructor(
    public estado: number,
    mensaje: string,
    public datos: any = null,
  ) {
    super(mensaje);
  }
}

type Oyente = (error: ErrorApi) => void;
const oyentesSesion = new Set<Oyente>();

/** Se avisa cuando la sesión expiró (401) o hay que cambiar la contraseña. */
export function alPerderSesion(oyente: Oyente) {
  oyentesSesion.add(oyente);
  return () => oyentesSesion.delete(oyente);
}

async function solicitar<T>(metodo: string, ruta: string, cuerpo?: unknown, opciones: { binario?: Blob; ignorar401?: boolean } = {}): Promise<T> {
  const encabezados: Record<string, string> = { Accept: 'application/json' };
  if (metodo !== 'GET') encabezados['X-HelpDesk'] = '1';
  let body: BodyInit | undefined;
  if (opciones.binario) {
    body = opciones.binario;
    encabezados['Content-Type'] = 'application/octet-stream';
  } else if (cuerpo !== undefined) {
    body = JSON.stringify(cuerpo);
    encabezados['Content-Type'] = 'application/json';
  }

  let respuesta: Response;
  try {
    respuesta = await fetch(ruta, { method: metodo, headers: encabezados, body, credentials: 'same-origin' });
  } catch {
    throw new ErrorApi(0, 'No hay conexión con el servidor de la Mesa de Ayuda. Revisá la red e intentá de nuevo.');
  }

  if (respuesta.status === 204) return undefined as T;

  const tipo = respuesta.headers.get('content-type') ?? '';
  const datos = tipo.includes('application/json') ? await respuesta.json().catch(() => null) : null;

  if (!respuesta.ok) {
    const mensaje =
      (datos && typeof datos.mensaje === 'string' && datos.mensaje) ||
      (respuesta.status === 401
        ? 'La sesión expiró. Volvé a ingresar.'
        : respuesta.status === 403
          ? 'No tenés permiso para esta acción.'
          : `El servidor respondió con un error (${respuesta.status}).`);
    const error = new ErrorApi(respuesta.status, mensaje, datos?.datos ?? null);
    const debeCambiar = respuesta.status === 403 && error.datos?.codigo === 'debe_cambiar_password';
    if ((respuesta.status === 401 && !opciones.ignorar401) || debeCambiar) {
      oyentesSesion.forEach((o) => o(error));
    }
    throw error;
  }
  return datos as T;
}

export const api = {
  get: <T>(ruta: string, opciones?: { ignorar401?: boolean }) => solicitar<T>('GET', ruta, undefined, opciones),
  post: <T>(ruta: string, cuerpo?: unknown) => solicitar<T>('POST', ruta, cuerpo ?? {}),
  put: <T>(ruta: string, cuerpo?: unknown) => solicitar<T>('PUT', ruta, cuerpo ?? {}),
  patch: <T>(ruta: string, cuerpo?: unknown) => solicitar<T>('PATCH', ruta, cuerpo ?? {}),
  delete: <T = void>(ruta: string) => solicitar<T>('DELETE', ruta),
  subir: <T>(ruta: string, archivo: Blob) => solicitar<T>('POST', ruta, undefined, { binario: archivo }),
};

/** Arma una query string ignorando valores vacíos. */
export function query(parametros: Record<string, string | number | boolean | null | undefined>) {
  const qs = new URLSearchParams();
  for (const [clave, valor] of Object.entries(parametros)) {
    if (valor === null || valor === undefined || valor === '' || valor === false) continue;
    qs.set(clave, String(valor));
  }
  const texto = qs.toString();
  return texto ? `?${texto}` : '';
}

/** Descarga un archivo generado por el servidor (exportaciones). */
export function descargar(ruta: string) {
  const enlace = document.createElement('a');
  enlace.href = ruta;
  enlace.rel = 'noopener';
  document.body.appendChild(enlace);
  enlace.click();
  enlace.remove();
}
