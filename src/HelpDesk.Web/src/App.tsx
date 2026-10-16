import { useCallback, useMemo, useState } from 'react';
import { Cargando } from './componentes/basicos';
import { MenuLateral } from './componentes/MenuLateral';
import {
  ProveedorAvisos,
  ProveedorCatalogos,
  ProveedorContadores,
  ProveedorNueva,
  ProveedorSesion,
  useSesion,
} from './estado/contextos';
import { CambiarPassword } from './paginas/CambiarPassword';
import { Configuracion } from './paginas/configuracion/Configuracion';
import { Dashboard } from './paginas/Dashboard';
import { DetalleSolicitud } from './paginas/DetalleSolicitud';
import { Login } from './paginas/Login';
import { NuevaSolicitud } from './paginas/NuevaSolicitud';
import { Pendientes } from './paginas/Pendientes';
import { Reportes } from './paginas/Reportes';
import { Solicitudes } from './paginas/Solicitudes';
import { TareasExtra } from './paginas/TareasExtra';
import { useTiempoReal } from './estado/tiempoReal';
import { useAtajo } from './util/hooks';
import { cambiarParams, Enlace, useUbicacion } from './util/rutas';

export function App() {
  return (
    <ProveedorAvisos>
      <ProveedorSesion>
        <Puerta />
      </ProveedorSesion>
    </ProveedorAvisos>
  );
}

function Puerta() {
  const { usuario, iniciando } = useSesion();
  if (iniciando) return <Cargando texto="Abriendo la Mesa de Ayuda…" />;
  if (!usuario) return <Login />;
  if (usuario.debeCambiarPassword) return <CambiarPassword obligatorio />;
  return (
    <ProveedorCatalogos>
      <ProveedorContadores>
        <Aplicacion />
      </ProveedorContadores>
    </ProveedorCatalogos>
  );
}

function Aplicacion() {
  const { usuario } = useSesion();
  const { ruta, params } = useUbicacion();
  const [nuevaAbierta, setNuevaAbierta] = useState(false);
  const abrirNueva = useCallback(() => setNuevaAbierta(true), []);
  const contextoNueva = useMemo(() => ({ abrir: abrirNueva }), [abrirNueva]);
  useAtajo('n', abrirNueva);
  useTiempoReal();

  const verId = Number(params.get('ver')) || null;
  const esAdmin = usuario?.rol === 'ADMIN';

  let pagina;
  if (ruta === '/') pagina = <Dashboard />;
  else if (ruta === '/solicitudes') pagina = <Solicitudes />;
  else if (ruta === '/pendientes') pagina = <Pendientes />;
  else if (ruta === '/tareas-extra') pagina = <TareasExtra />;
  else if (ruta === '/reportes' && esAdmin) pagina = <Reportes />;
  else if (ruta.startsWith('/configuracion') && esAdmin) pagina = <Configuracion seccion={ruta.split('/')[2] || 'responsables'} />;
  else if (ruta === '/cuenta') pagina = <CambiarPassword />;
  else
    pagina = (
      <div className="contenido" style={{ paddingTop: 40 }}>
        <h1>Esta página no existe</h1>
        <p>
          Volvé al <Enlace a="/">inicio</Enlace> o abrí la <Enlace a="/solicitudes">lista de solicitudes</Enlace>.
        </p>
      </div>
    );

  return (
    <ProveedorNueva value={contextoNueva}>
      <div className="app">
        <MenuLateral />
        <main className="principal">{pagina}</main>
      </div>
      <NuevaSolicitud abierto={nuevaAbierta} onCerrar={() => setNuevaAbierta(false)} />
      <DetalleSolicitud id={verId} onCerrar={() => cambiarParams({ ver: null })} />
    </ProveedorNueva>
  );
}
