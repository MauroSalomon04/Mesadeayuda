import { useState } from 'react';
import { LuKeyRound, LuPlus } from 'react-icons/lu';
import { api } from '../../api';
import { Dialogo, ErrorCaja } from '../../componentes/basicos';
import { mensajeError, useAvisar, useCatalogos, useUsuario } from '../../estado/contextos';
import type { Rol, UsuarioVista } from '../../tipos';
import { fechaHora } from '../../util/formato';
import { useCarga } from '../../util/hooks';

export function Usuarios() {
  const yo = useUsuario();
  const { datos, error, recargar } = useCarga(() => api.get<UsuarioVista[]>('/api/admin/usuarios'), []);
  const [editando, setEditando] = useState<UsuarioVista | 'nuevo' | null>(null);
  const [clave, setClave] = useState<UsuarioVista | null>(null);

  return (
    <div className="tarjeta" style={{ maxWidth: 1000 }}>
      <div style={{ display: 'flex', alignItems: 'center', marginBottom: 12, gap: 12 }}>
        <p className="tenue" style={{ margin: 0 }}>
          <strong>Administrador</strong>: configuración, usuarios, importación y reportes. <strong>Soporte</strong>: registrar, editar, resolver y buscar solicitudes.
        </p>
        <button type="button" className="btn btn-primario" style={{ marginLeft: 'auto' }} onClick={() => setEditando('nuevo')}>
          <LuPlus /> Nuevo usuario
        </button>
      </div>
      <ErrorCaja error={error} reintentar={recargar} />
      <table className="tabla-simple">
        <thead>
          <tr>
            <th>Usuario</th>
            <th>Nombre</th>
            <th>Rol</th>
            <th>Responsable vinculado</th>
            <th>Último ingreso</th>
            <th>Situación</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {datos?.map((u) => (
            <tr key={u.id} className={u.activo ? '' : 'inactivo'}>
              <td className="codigo">{u.usuario}</td>
              <td>
                {u.nombre}
                {u.id === yo.id && <span className="tenue"> (vos)</span>}
              </td>
              <td>{u.rol === 'ADMIN' ? 'Administrador' : 'Soporte'}</td>
              <td>{u.responsable ?? <span className="tenue">—</span>}</td>
              <td>{u.ultimoAcceso ? fechaHora(u.ultimoAcceso) : <span className="tenue">Nunca</span>}</td>
              <td>
                {!u.activo ? (
                  <span className="etiqueta-estado">Desactivado</span>
                ) : u.debeCambiarPassword ? (
                  <span className="etiqueta-estado alerta">Con contraseña temporal</span>
                ) : (
                  <span className="etiqueta-estado ok">Activo</span>
                )}
              </td>
              <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                <button type="button" className="btn btn-chico btn-sutil" onClick={() => setClave(u)}>
                  <LuKeyRound /> Restablecer contraseña
                </button>
                <button type="button" className="btn btn-chico" onClick={() => setEditando(u)}>
                  Editar
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {editando && <EditorUsuario usuario={editando === 'nuevo' ? null : editando} onCerrar={() => setEditando(null)} onGuardado={recargar} />}
      {clave && <RestablecerClave usuario={clave} onCerrar={() => setClave(null)} onGuardado={recargar} />}
    </div>
  );
}

function EditorUsuario({ usuario, onCerrar, onGuardado }: { usuario: UsuarioVista | null; onCerrar: () => void; onGuardado: () => void }) {
  const catalogos = useCatalogos();
  const avisar = useAvisar();
  const [nombreUsuario, setNombreUsuario] = useState(usuario?.usuario ?? '');
  const [nombre, setNombre] = useState(usuario?.nombre ?? '');
  const [rol, setRol] = useState<Rol>(usuario?.rol ?? 'SOPORTE');
  const [responsableId, setResponsableId] = useState<number | ''>(usuario?.responsableId ?? '');
  const [activo, setActivo] = useState(usuario?.activo ?? true);
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);

  const guardar = async () => {
    setError(null);
    const cuerpo = { usuario: nombreUsuario, nombre, rol, responsableId: responsableId === '' ? null : responsableId, activo, password };
    try {
      if (usuario) await api.put(`/api/admin/usuarios/${usuario.id}`, cuerpo);
      else await api.post('/api/admin/usuarios', cuerpo);
      avisar(usuario ? 'Usuario actualizado.' : `Usuario “${nombreUsuario.trim().toLowerCase()}” creado. Al ingresar se le pedirá cambiar la contraseña.`);
      onGuardado();
      onCerrar();
    } catch (e) {
      setError(mensajeError(e));
    }
  };

  return (
    <Dialogo
      abierto
      onCerrar={onCerrar}
      titulo={usuario ? `Editar ${usuario.usuario}` : 'Nuevo usuario'}
      pie={
        <>
          <button type="button" className="btn btn-sutil" onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primario" onClick={() => void guardar()}>
            {usuario ? 'Guardar cambios' : 'Crear usuario'}
          </button>
        </>
      }
    >
      <div className="dialogo-cuerpo" style={{ display: 'grid', gap: 12 }}>
        {!usuario && (
          <div className="campo">
            <label htmlFor="u-usuario">Usuario (para ingresar)</label>
            <input id="u-usuario" className="entrada" value={nombreUsuario} onChange={(e) => setNombreUsuario(e.target.value)} autoFocus placeholder="ej.: mauro" />
          </div>
        )}
        <div className="campo">
          <label htmlFor="u-nombre">Nombre completo</label>
          <input id="u-nombre" className="entrada" value={nombre} onChange={(e) => setNombre(e.target.value)} />
        </div>
        <div className="campo">
          <span className="campo-etiqueta">Rol</span>
          <div className="seg">
            {(['SOPORTE', 'ADMIN'] as Rol[]).map((r) => (
              <label key={r} className={`seg-opcion${rol === r ? ' marcada' : ''}`}>
                <input type="radio" name="rol" checked={rol === r} onChange={() => setRol(r)} />
                {r === 'ADMIN' ? 'Administrador' : 'Soporte'}
              </label>
            ))}
          </div>
        </div>
        <div className="campo">
          <label htmlFor="u-resp">Responsable vinculado</label>
          <select id="u-resp" className="selector" value={responsableId} onChange={(e) => setResponsableId(e.target.value === '' ? '' : Number(e.target.value))}>
            <option value="">Ninguno</option>
            {catalogos.responsables.map((r) => (
              <option key={r.id} value={r.id}>
                {r.nombre}
                {r.activo ? '' : ' (desactivado)'}
              </option>
            ))}
          </select>
          <span className="campo-ayuda">Se usa para “Mis solicitudes” y como responsable predeterminado al registrar.</span>
        </div>
        {!usuario && (
          <div className="campo">
            <label htmlFor="u-pass">Contraseña temporal</label>
            <input id="u-pass" className="entrada" type="text" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="off" />
            <span className="campo-ayuda">Al menos 8 caracteres. El usuario la cambia en su primer ingreso.</span>
          </div>
        )}
        {usuario && (
          <label className="seg-opcion" style={{ width: 'fit-content' }}>
            <input type="checkbox" checked={activo} onChange={(e) => setActivo(e.target.checked)} />
            <span className="casilla">{activo && '✓'}</span>
            Puede ingresar al sistema
          </label>
        )}
        {error && <div className="error-caja">{error}</div>}
      </div>
    </Dialogo>
  );
}

function RestablecerClave({ usuario, onCerrar, onGuardado }: { usuario: UsuarioVista; onCerrar: () => void; onGuardado: () => void }) {
  const avisar = useAvisar();
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const guardar = async () => {
    try {
      await api.post(`/api/admin/usuarios/${usuario.id}/restablecer-password`, { password });
      avisar(`Contraseña temporal asignada a ${usuario.usuario}. Se cerraron sus sesiones abiertas.`);
      onGuardado();
      onCerrar();
    } catch (e) {
      setError(mensajeError(e));
    }
  };
  return (
    <Dialogo
      abierto
      onCerrar={onCerrar}
      titulo={`Restablecer contraseña de ${usuario.usuario}`}
      pie={
        <>
          <button type="button" className="btn btn-sutil" onClick={onCerrar}>
            Cancelar
          </button>
          <button type="button" className="btn btn-primario" onClick={() => void guardar()}>
            Asignar contraseña temporal
          </button>
        </>
      }
    >
      <div className="dialogo-cuerpo" style={{ display: 'grid', gap: 12 }}>
        <div className="campo">
          <label htmlFor="r-pass">Contraseña temporal</label>
          <input id="r-pass" className="entrada" type="text" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="off" autoFocus />
          <span className="campo-ayuda">Comunicásela a la persona. Tendrá que cambiarla al ingresar.</span>
        </div>
        {error && <div className="error-caja">{error}</div>}
      </div>
    </Dialogo>
  );
}
