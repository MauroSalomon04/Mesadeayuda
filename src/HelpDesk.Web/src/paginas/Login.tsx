import { FormEvent, useState } from 'react';
import { mensajeError, useSesion } from '../estado/contextos';

export function Login() {
  const { ingresar } = useSesion();
  const [usuario, setUsuario] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  const enviar = async (e: FormEvent) => {
    e.preventDefault();
    setError(null);
    setEnviando(true);
    try {
      await ingresar(usuario.trim(), password);
    } catch (err) {
      setError(mensajeError(err));
      setPassword('');
    } finally {
      setEnviando(false);
    }
  };

  return (
    <div className="login">
      <div className="login-caja">
        <div className="login-marca">
          <div className="marca-nombre">Mesa de Ayuda</div>
          <div className="marca-sub">Registro de solicitudes de IntegradoC en ASSE</div>
        </div>
        <form className="login-form" onSubmit={enviar}>
          <div className="campo">
            <label htmlFor="usuario">Usuario</label>
            <input
              id="usuario"
              className="entrada"
              value={usuario}
              onChange={(e) => setUsuario(e.target.value)}
              autoComplete="username"
              autoFocus
              required
            />
          </div>
          <div className="campo">
            <label htmlFor="password">Contraseña</label>
            <input
              id="password"
              type="password"
              className="entrada"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              autoComplete="current-password"
              required
            />
          </div>
          {error && (
            <div className="error-caja" role="alert">
              {error}
            </div>
          )}
          <button type="submit" className="btn btn-primario" disabled={enviando}>
            {enviando ? 'Ingresando…' : 'Ingresar'}
          </button>
        </form>
        <p className="tenue" style={{ fontSize: 'var(--t-12)', marginTop: 14 }}>
          Uso interno de la mesa de ayuda. Si olvidaste tu contraseña, pedile a un administrador que la restablezca.
        </p>
      </div>
    </div>
  );
}
