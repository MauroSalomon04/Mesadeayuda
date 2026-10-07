import {
  LuBriefcase,
  LuChartColumn,
  LuInbox,
  LuKeyRound,
  LuLayoutDashboard,
  LuListTodo,
  LuLogOut,
  LuPlus,
  LuSettings,
  LuUser,
} from 'react-icons/lu';
import type { ReactNode } from 'react';
import { useContadores, useNuevaSolicitud, useSesion } from '../estado/contextos';
import { numero } from '../util/formato';
import { Enlace, useUbicacion } from '../util/rutas';

export const SECCIONES_CONFIGURACION = [
  { clave: 'responsables', titulo: 'Responsables' },
  { clave: 'tipos', titulo: 'Tipos de solicitud' },
  { clave: 'medios', titulo: 'Medios de contacto' },
  { clave: 'estados', titulo: 'Estados' },
  { clave: 'prioridades', titulo: 'Prioridades' },
  { clave: 'oficinas', titulo: 'Oficinas' },
  { clave: 'areas', titulo: 'Áreas de ASSE' },
  { clave: 'usuarios', titulo: 'Usuarios' },
  { clave: 'importar', titulo: 'Importar Excel' },
] as const;

export function MenuLateral() {
  const { usuario, salir } = useSesion();
  const { contadores } = useContadores();
  const { abrir } = useNuevaSolicitud();
  const { ruta, params } = useUbicacion();
  const rapido = params.get('rapido');
  const esAdmin = usuario?.rol === 'ADMIN';

  const item = (a: string, activo: boolean, icono: ReactNode, texto: string, contador?: number | null, alerta = false) => (
    <Enlace a={a} className={`menu-item${activo ? ' activo' : ''}`} aria-current={activo ? 'page' : undefined} title={texto}>
      {icono}
      <span className="menu-texto">{texto}</span>
      {contador !== undefined && contador !== null && contador > 0 && (
        <span className={`menu-contador${alerta ? ' alerta' : ''}`}>{numero(contador)}</span>
      )}
    </Enlace>
  );

  return (
    <nav className="menu" aria-label="Menú principal">
      <div className="marca">
        <div className="marca-icono" aria-hidden>
          MA
        </div>
        <div className="marca-nombre">Mesa de Ayuda</div>
        <div className="marca-sub">IntegradoC en ASSE</div>
      </div>

      <button type="button" className="btn btn-primario menu-nueva" onClick={abrir} title="Nueva solicitud (tecla N)">
        <LuPlus /> <span className="menu-texto">Nueva solicitud</span>
        <span className="tecla menu-texto" style={{ marginLeft: 'auto' }}>
          N
        </span>
      </button>

      {item('/', ruta === '/', <LuLayoutDashboard />, 'Inicio')}

      <div className="menu-grupo">
        <div className="menu-grupo-titulo">Solicitudes</div>
        {item('/solicitudes', ruta === '/solicitudes' && rapido !== 'mias', <LuInbox />, 'Todas')}
        {item('/pendientes', ruta === '/pendientes', <LuListTodo />, 'Pendientes', contadores?.pendientes, true)}
        {item('/solicitudes?rapido=mias', ruta === '/solicitudes' && rapido === 'mias', <LuUser />, 'Mis solicitudes', contadores?.misPendientes, true)}
      </div>

      <div className="menu-grupo">
        {item('/tareas-extra', ruta === '/tareas-extra', <LuBriefcase />, 'Tareas extra')}
        {esAdmin && item('/reportes', ruta === '/reportes', <LuChartColumn />, 'Reportes')}
        {esAdmin && item('/configuracion/responsables', ruta.startsWith('/configuracion'), <LuSettings />, 'Configuración')}
        {esAdmin && ruta.startsWith('/configuracion') && (
          <div className="menu-sub">
            {SECCIONES_CONFIGURACION.map((s) => (
              <Enlace
                key={s.clave}
                a={`/configuracion/${s.clave}`}
                className={`menu-item${ruta === `/configuracion/${s.clave}` || (ruta === '/configuracion' && s.clave === 'responsables') ? ' activo' : ''}`}
              >
                {s.titulo}
              </Enlace>
            ))}
          </div>
        )}
      </div>

      <div className="menu-pie">
        <div className="menu-usuario">
          <strong>{usuario?.nombre}</strong>
          <span className="tenue">{usuario?.rol === 'ADMIN' ? 'Administrador' : 'Soporte'}</span>
        </div>
        {item('/cuenta', ruta === '/cuenta', <LuKeyRound />, 'Cambiar contraseña')}
        <button type="button" className="menu-item" onClick={() => void salir()} title="Salir">
          <LuLogOut /> <span className="menu-texto">Salir</span>
        </button>
      </div>
    </nav>
  );
}
