using System.Collections.Concurrent;

namespace HelpDesk.Api.Infraestructura;

/// <summary>
/// Bloquea temporalmente un usuario/IP después de varios intentos fallidos de login.
/// </summary>
public sealed class LimitadorLogin
{
    private const int MaximoFallos = 5;
    private static readonly TimeSpan Ventana = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Bloqueo = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Registro> _registros = new();

    private sealed class Registro
    {
        public int Fallos;
        public DateTime PrimerFalloUtc;
        public DateTime? BloqueadoHastaUtc;
    }

    public bool EstaBloqueado(string clave, out TimeSpan espera)
    {
        espera = TimeSpan.Zero;
        if (!_registros.TryGetValue(clave, out var registro)) return false;
        lock (registro)
        {
            if (registro.BloqueadoHastaUtc is DateTime hasta && hasta > DateTime.UtcNow)
            {
                espera = hasta - DateTime.UtcNow;
                return true;
            }
        }
        return false;
    }

    public void RegistrarFallo(string clave)
    {
        var registro = _registros.GetOrAdd(clave, _ => new Registro { PrimerFalloUtc = DateTime.UtcNow });
        lock (registro)
        {
            var ahora = DateTime.UtcNow;
            if (ahora - registro.PrimerFalloUtc > Ventana)
            {
                registro.Fallos = 0;
                registro.PrimerFalloUtc = ahora;
                registro.BloqueadoHastaUtc = null;
            }
            registro.Fallos++;
            if (registro.Fallos >= MaximoFallos)
            {
                registro.BloqueadoHastaUtc = ahora + Bloqueo;
                registro.Fallos = 0;
                registro.PrimerFalloUtc = ahora;
            }
        }

        // Limpieza ocasional para no acumular entradas viejas.
        if (_registros.Count > 1000)
        {
            var limite = DateTime.UtcNow - Ventana;
            foreach (var par in _registros)
            {
                if (par.Value.PrimerFalloUtc < limite && (par.Value.BloqueadoHastaUtc is null || par.Value.BloqueadoHastaUtc < DateTime.UtcNow))
                {
                    _registros.TryRemove(par.Key, out _);
                }
            }
        }
    }

    public void Limpiar(string clave) => _registros.TryRemove(clave, out _);
}
