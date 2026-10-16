import { FormEvent, useState } from 'react';
import { api } from '../api';
import { mensajeError, useAvisar, useSesion } from '../estado/contextos';
import type { Sesion } from '../tipos';

export function CambiarPassword({ obligatorio = false }: { obligatorio?: boolean }) {
  const { actualizar, salir, usuario } = useSesion();
  const avisar = useAvisar();
  const [actual, setActual] = useState('');
  const [nueva, setNueva] = useState('');
  const [repetida, setRepetida] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  const enviar = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    if (nueva.length < 8) return setError('La nueva contraseña debe tener al menos 8 caracteres.');
    if (nueva !== repetida) return setError('Las dos contraseñas nuevas no coinciden.');
    setEnviando(true);
    try {
      const sesion = await api.post<Sesion>('/api/auth/cambiar-password', { actual, nueva });
      actualizar(sesion);
      setActual('');
      setNueva('');
      setRepetida('');
      avisar('Contraseña actualizada.');
    } catch (err) {
      setError(mensajeError(err));
    } finally {
      setEnviando(false);
    }
  };

  const formulario = (
    <form className="login-form" onSubmit={enviar}>
      <div className="campo">
        <label htmlFor="pw-actual">{obligatorio ? 'Contraseña temporal' : 'Contraseña actual'}</label>
        <input id="pw-actual" type="password" className="entrada" value={actual} onChange={(e) => setActual(e.target.value)} autoComplete="current-password" required autoFocus />
      </div>
      <div className="campo">
        <label htmlFor="pw-nueva">Nueva contraseña</label>
        <input id="pw-nueva" type="password" className="entrada" value={nueva} onChange={(e) => setNueva(e.target.value)} autoComplete="new-password" required />
        <span className="campo-ayuda">Al menos 8 caracteres.</span>
      </div>
      <div className="campo">
        <label htmlFor="pw-repetida">Repetí la nueva contraseña</label>
        <input id="pw-repetida" type="password" className="entrada" value={repetida} onChange={(e) => setRepetida(e.target.value)} autoComplete="new-password" required />
      </div>
      {error && (
        <div className="error-caja" role="alert">
          {error}
        </div>
      )}
      <button type="submit" className="btn btn-primario" disabled={enviando}>
        {enviando ? 'Guardando…' : 'Cambiar contraseña'}
      </button>
      {obligatorio && (
        <button type="button" className="btn btn-sutil" onClick={() => void salir()}>
          Salir
        </button>
      )}
    </form>
  );

  if (obligatorio) {
    return (
      <div className="login">
        <div className="login-caja">
          <div className="login-marca">
            <div className="marca-nombre">Hola, {usuario?.nombre}</div>
            <div className="marca-sub">Antes de empezar, elegí una contraseña propia para reemplazar la temporal.</div>
          </div>
          {formulario}
        </div>
      </div>
    );
  }

  return (
    <>
      <header className="encabezado">
        <h1>Cambiar contraseña</h1>
      </header>
      <div className="contenido" style={{ maxWidth: 440 }}>
        {formulario}
      </div>
    </>
  );
}
