using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Features;

namespace HelpDesk.Api.Modulos.TiempoReal;

/// <summary>Qué datos cambiaron: la interfaz vuelve a pedir solo lo afectado.</summary>
public static class TiposCambio
{
    public const string Solicitudes = "solicitudes";
    public const string Catalogos = "catalogos";
    public const string Tareas = "tareas";
}

/// <summary>
/// Publica cambios con NOTIFY de PostgreSQL. Si se llama dentro de una transacción, PostgreSQL solo
/// entrega el aviso cuando la transacción se confirma (nunca se avisa un cambio revertido) y llega a
/// todas las instancias del backend conectadas a la misma base.
/// </summary>
public sealed class Notificador(IHttpContextAccessor accesoHttp)
{
    public const string Canal = "helpdesk_cambios";
    public const string EncabezadoCliente = "X-HelpDesk-Cliente";

    public Task PublicarAsync(IDbConnection cn, IDbTransaction? tx, string[] tipos, string accion, int? id = null)
    {
        // Pestaña que originó el cambio: ella ya actualizó su pantalla y puede ignorar el aviso.
        var origen = (string?)accesoHttp.HttpContext?.Request.Headers[EncabezadoCliente];
        if (origen is not null && (origen.Length > 64 || !origen.All(char.IsAsciiLetterOrDigit))) origen = null;
        var datos = JsonSerializer.Serialize(new { tipos, accion, id, origen }, JsonSerializerOptions.Web);
        return cn.ExecuteAsync("SELECT pg_notify(@canal, @datos)", new { canal = Canal, datos }, tx);
    }
}

/// <summary>
/// Reparte los avisos a las conexiones abiertas (Server-Sent Events). Guarda los últimos avisos para
/// que un navegador que se reconecta reciba lo que se perdió (encabezado Last-Event-ID).
/// </summary>
public sealed class CanalCambios
{
    public sealed record Aviso(long Numero, string Datos);

    public sealed class Suscripcion(CanalCambios canal, Channel<Aviso> cola, List<Aviso> perdidos, bool resincronizar, long ultimo) : IDisposable
    {
        public ChannelReader<Aviso> Lector => cola.Reader;
        public IReadOnlyList<Aviso> Perdidos => perdidos;
        /// <summary>No se pueden recuperar los avisos perdidos: la interfaz debe recargar todo.</summary>
        public bool Resincronizar => resincronizar;
        /// <summary>Último aviso emitido al suscribirse: desde ahí sigue la cola.</summary>
        public long Ultimo => ultimo;
        public void Dispose() => canal.Quitar(cola);
    }

    private const int AvisosGuardados = 500;
    private readonly Lock _bloqueo = new();
    private readonly List<Channel<Aviso>> _colas = [];
    private readonly Queue<Aviso> _recientes = new();
    private long _ultimo;

    /// <summary>Identifica este proceso: tras un reinicio los números de aviso anteriores no valen.</summary>
    public string Instancia { get; } = Guid.NewGuid().ToString("N")[..10];

    public int Conexiones { get { lock (_bloqueo) return _colas.Count; } }

    public void Difundir(string datos)
    {
        lock (_bloqueo)
        {
            var aviso = new Aviso(++_ultimo, datos);
            _recientes.Enqueue(aviso);
            if (_recientes.Count > AvisosGuardados) _recientes.Dequeue();
            foreach (var cola in _colas) cola.Writer.TryWrite(aviso);
        }
    }

    public Suscripcion Suscribir(string? ultimoRecibido)
    {
        var cola = Channel.CreateBounded<Aviso>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        lock (_bloqueo)
        {
            var perdidos = new List<Aviso>();
            var resincronizar = false;
            if (!string.IsNullOrEmpty(ultimoRecibido))
            {
                var partes = ultimoRecibido.Split(':');
                var primero = _recientes.Count > 0 ? _recientes.Peek().Numero : _ultimo + 1;
                if (partes.Length == 2 && partes[0] == Instancia &&
                    long.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) &&
                    numero >= primero - 1 && numero <= _ultimo)
                {
                    perdidos.AddRange(_recientes.Where(a => a.Numero > numero));
                }
                else
                {
                    resincronizar = true;
                }
            }
            _colas.Add(cola);
            return new Suscripcion(this, cola, perdidos, resincronizar, _ultimo);
        }
    }

    private void Quitar(Channel<Aviso> cola)
    {
        lock (_bloqueo) _colas.Remove(cola);
        cola.Writer.TryComplete();
    }
}

/// <summary>
/// Escucha el canal de PostgreSQL (LISTEN) con una conexión dedicada y reenvía cada aviso a
/// <see cref="CanalCambios"/>. Si la conexión se corta, reintenta y pide a las pantallas que recarguen.
/// </summary>
public sealed class EscuchaCambios(BaseDatos db, CanalCambios canal, ILogger<EscuchaCambios> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var constructor = new NpgsqlConnectionStringBuilder(db.CadenaConexion)
        {
            Pooling = false,     // conexión propia: no vuelve al pool mientras escucha
            KeepAlive = 30,      // detecta cortes de red aunque no haya avisos
        };
        var huboCorte = false;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var cn = new NpgsqlConnection(constructor.ConnectionString);
                cn.Notification += (_, e) => canal.Difundir(e.Payload);
                await cn.OpenAsync(ct);
                await using (var comando = new NpgsqlCommand($"LISTEN {Notificador.Canal}", cn))
                {
                    await comando.ExecuteNonQueryAsync(ct);
                }
                if (huboCorte)
                {
                    log.LogInformation("Actualización en tiempo real restablecida.");
                    // Mientras no se escuchaba se pudieron perder avisos.
                    canal.Difundir(JsonSerializer.Serialize(new { tipos = new[] { "todo" }, accion = "resincronizar" }, JsonSerializerOptions.Web));
                    huboCorte = false;
                }
                while (!ct.IsCancellationRequested)
                {
                    await cn.WaitAsync(ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!huboCorte) log.LogWarning("Se perdió la escucha de cambios de PostgreSQL: {Mensaje}. Reintentando...", ex.Message);
                huboCorte = true;
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}

public static class TiempoRealEndpoints
{
    // Cada tanto se cierra la conexión: el navegador se reconecta solo (recibiendo lo que se perdió)
    // y en la reconexión se vuelve a validar la sesión.
    private static readonly TimeSpan DuracionConexion = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan IntervaloLatido = TimeSpan.FromSeconds(20);

    public static void MapearTiempoReal(this RouteGroupBuilder api)
    {
        api.MapGet("/eventos", Eventos);
    }

    private static async Task Eventos(HttpContext http, CanalCambios canal, IHostApplicationLifetime vida)
    {
        var respuesta = http.Response;
        respuesta.ContentType = "text/event-stream; charset=utf-8";
        respuesta.Headers.CacheControl = "no-store";
        respuesta.Headers["X-Accel-Buffering"] = "no"; // nginx: no acumular la respuesta
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var suscripcion = canal.Suscribir(http.Request.Headers["Last-Event-ID"]);
        using var fin = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, vida.ApplicationStopping);
        fin.CancelAfter(DuracionConexion);

        try
        {
            await respuesta.WriteAsync("retry: 3000\n\n", fin.Token);
            if (suscripcion.Resincronizar)
            {
                await respuesta.WriteAsync("event: resincronizar\ndata: {}\n\n", fin.Token);
            }
            foreach (var aviso in suscripcion.Perdidos)
            {
                await EscribirAsync(respuesta, canal, aviso, fin.Token);
            }
            // Posición actual: si la conexión se corta, el navegador la envía como Last-Event-ID
            // y al reconectarse recibe exactamente los avisos que se perdió.
            await EscribirAsync(respuesta, canal, new CanalCambios.Aviso(suscripcion.Ultimo, """{"tipos":[]}"""), fin.Token);
            await respuesta.Body.FlushAsync(fin.Token);

            while (!fin.IsCancellationRequested)
            {
                using var espera = CancellationTokenSource.CreateLinkedTokenSource(fin.Token);
                espera.CancelAfter(IntervaloLatido);
                try
                {
                    if (!await suscripcion.Lector.WaitToReadAsync(espera.Token)) break;
                    while (suscripcion.Lector.TryRead(out var aviso))
                    {
                        await EscribirAsync(respuesta, canal, aviso, fin.Token);
                    }
                }
                catch (OperationCanceledException) when (!fin.IsCancellationRequested)
                {
                    // Latido: mantiene viva la conexión a través de proxies (nginx, Render).
                    await respuesta.WriteAsync(": latido\n\n", fin.Token);
                }
                await respuesta.Body.FlushAsync(fin.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // El navegador cerró la pestaña, se alcanzó la duración máxima o el servidor se detiene.
        }
    }

    private static Task EscribirAsync(HttpResponse respuesta, CanalCambios canal, CanalCambios.Aviso aviso, CancellationToken ct) =>
        respuesta.WriteAsync($"id: {canal.Instancia}:{aviso.Numero}\ndata: {aviso.Datos}\n\n", ct);
}
