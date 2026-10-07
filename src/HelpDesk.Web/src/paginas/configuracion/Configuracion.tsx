import { SECCIONES_CONFIGURACION } from '../../componentes/MenuLateral';
import { Enlace } from '../../util/rutas';
import { Catalogo } from './Catalogo';
import { Importar } from './Importar';
import { Oficinas } from './Oficinas';
import { Usuarios } from './Usuarios';

const DESCRIPCIONES: Record<string, string> = {
  responsables: 'Integrantes de la mesa de ayuda a los que se asignan las solicitudes. Desactivá a quien ya no está para que no aparezca en el formulario, sin perder su historial.',
  tipos: 'Códigos de tipo de solicitud tal como los usa la mesa (AD, FFRRHH, Seg…). La descripción es opcional y aparece al pasar el mouse.',
  medios: 'Por dónde llegó la solicitud. El predeterminado se marca solo en cada solicitud nueva.',
  estados: 'Los estados marcados como “resuelto” cierran la solicitud y calculan el tiempo de resolución. Los demás cuentan como pendientes.',
  prioridades: 'El nivel ordena las prioridades: 1 es la más urgente.',
  areas: 'Áreas de ASSE con las que se colabora en las tareas extra.',
};

export function Configuracion({ seccion }: { seccion: string }) {
  const titulo = SECCIONES_CONFIGURACION.find((s) => s.clave === seccion)?.titulo ?? 'Configuración';
  let contenido;
  if (seccion === 'usuarios') contenido = <Usuarios />;
  else if (seccion === 'oficinas') contenido = <Oficinas />;
  else if (seccion === 'importar') contenido = <Importar />;
  else if (DESCRIPCIONES[seccion]) contenido = <Catalogo clave={seccion} />;
  else contenido = <p>Sección desconocida.</p>;

  return (
    <>
      <header className="encabezado">
        <div style={{ marginRight: 'auto' }}>
          <h1>{titulo}</h1>
          {DESCRIPCIONES[seccion] && <div className="encabezado-sub" style={{ maxWidth: 760 }}>{DESCRIPCIONES[seccion]}</div>}
        </div>
      </header>
      <div className="contenido">
        <nav className="pestanas config-pestanas" aria-label="Secciones de configuración">
          {SECCIONES_CONFIGURACION.map((s) => (
            <Enlace key={s.clave} a={`/configuracion/${s.clave}`} className={`pestana${s.clave === seccion ? ' activa' : ''}`}>
              {s.titulo}
            </Enlace>
          ))}
        </nav>
        {contenido}
      </div>
    </>
  );
}
