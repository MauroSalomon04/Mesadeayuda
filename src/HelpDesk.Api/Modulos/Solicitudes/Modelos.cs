namespace HelpDesk.Api.Modulos.Solicitudes;

/// <summary>Una fila de la tabla principal (equivale a una fila del Excel).</summary>
public class SolicitudFila
{
    /// <summary>Clave interna permanente (rutas, historial). Nunca se reutiliza.</summary>
    public int Id { get; set; }
    /// <summary>Número visible de la solicitud. Al eliminarla queda libre y se reutiliza.</summary>
    public int Numero { get; set; }
    public DateTime FechaIngreso { get; set; }
    public int DiaSemana { get; set; }
    public string? NombreFuncionario { get; set; }
    public int? OficinaId { get; set; }
    public string? Oficina { get; set; }
    public int? MedioContactoId { get; set; }
    public string? MedioContacto { get; set; }
    public int? TipoSolicitudId { get; set; }
    public string? TipoSolicitud { get; set; }
    public string? Descripcion { get; set; }
    public string? Observaciones { get; set; }
    public int? ResponsableId { get; set; }
    public string? Responsable { get; set; }
    public int? DuracionEstimadaMin { get; set; }
    public int? EstadoId { get; set; }
    public string? Estado { get; set; }
    public bool? EstadoEsResuelto { get; set; }
    public string? EstadoColor { get; set; }
    public int? PrioridadId { get; set; }
    public string? Prioridad { get; set; }
    public int? PrioridadNivel { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public int? MinutosResolucion { get; set; }
    public string Origen { get; set; } = "APP";
    public int Version { get; set; }
}

/// <summary>Solicitud completa con auditoría e historial.</summary>
public sealed class SolicitudDetalle : SolicitudFila
{
    public string? CreadoPor { get; set; }
    public DateTime CreadoEn { get; set; }
    public string? ActualizadoPor { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public int? FilaExcel { get; set; }
    public int? ImportacionId { get; set; }
    public List<HistorialItem> Historial { get; set; } = [];
}

public sealed class HistorialItem
{
    public long Id { get; set; }
    public DateTime FechaHora { get; set; }
    public string? Usuario { get; set; }
    public string Accion { get; set; } = "";
    public string? Campo { get; set; }
    public string? ValorAnterior { get; set; }
    public string? ValorNuevo { get; set; }
    public string? Detalle { get; set; }
}

/// <summary>Fila cruda de Solicitud (para actualizar).</summary>
public sealed class SolicitudRegistro
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public DateTime FechaIngreso { get; set; }
    public string? NombreFuncionario { get; set; }
    public int? OficinaId { get; set; }
    public int? MedioContactoId { get; set; }
    public int? TipoSolicitudId { get; set; }
    public string? Descripcion { get; set; }
    public string? Observaciones { get; set; }
    public int? ResponsableId { get; set; }
    public int? DuracionEstimadaMin { get; set; }
    public int? EstadoId { get; set; }
    public int? PrioridadId { get; set; }
    public DateTime? FechaResolucion { get; set; }
    public int? MinutosResolucion { get; set; }
    public int Version { get; set; }
    public int? ActualizadoPorId { get; set; }
    public DateTime ActualizadoEn { get; set; }
}

/// <summary>Datos del formulario "Nueva solicitud".</summary>
public sealed class SolicitudEntrada
{
    public string? NombreFuncionario { get; set; }
    public string? Oficina { get; set; }
    public int? MedioContactoId { get; set; }
    public int? TipoSolicitudId { get; set; }
    public string? Descripcion { get; set; }
    public string? Observaciones { get; set; }
    public int? ResponsableId { get; set; }
    public int? DuracionEstimadaMin { get; set; }
    public int? EstadoId { get; set; }
    public int? PrioridadId { get; set; }
}

/// <summary>
/// Edición parcial. "Cambios" trae solo los campos modificados; "Base" trae el valor que el
/// usuario tenía en pantalla antes de editar, para detectar si otra persona lo cambió.
/// </summary>
public sealed class ActualizacionEntrada
{
    public Dictionary<string, JsonElement>? Cambios { get; set; }
    public Dictionary<string, JsonElement>? Base { get; set; }
}

public sealed class ResolverEntrada
{
    public string? Observaciones { get; set; }
    public int? DuracionEstimadaMin { get; set; }
}

public sealed class CorreccionFechaEntrada
{
    public DateTime? FechaIngreso { get; set; }
    public string? Motivo { get; set; }
}

public sealed class PaginaResultado<T>
{
    public List<T> Items { get; set; } = [];
    public int Total { get; set; }
    public int Pagina { get; set; }
    public int Tamano { get; set; }
}

public sealed class Contadores
{
    public int Pendientes { get; set; }
    public int MisPendientes { get; set; }
    public int SinEstado { get; set; }
    public int Hoy { get; set; }
}

/// <summary>Un cambio que se registrará en SolicitudHistorial.</summary>
public sealed record CambioHistorial(string Accion, string? Campo, string? ValorAnterior, string? ValorNuevo, string? Detalle);
