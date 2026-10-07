namespace HelpDesk.Api.Infraestructura;

/// <summary>
/// Hora oficial de la aplicación. Todas las fechas se guardan como hora local
/// de la zona configurada (por defecto America/Montevideo), sin desplazamiento.
/// </summary>
public sealed class Reloj
{
    private static readonly string[] NombresDias =
        ["", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado", "Domingo"];

    private readonly TimeZoneInfo _zona;

    public Reloj(IOptions<OpcionesHelpDesk> opciones)
    {
        _zona = ObtenerZona(opciones.Value.ZonaHoraria);
    }

    /// <summary>Fecha y hora local actual, sin milisegundos.</summary>
    public DateTime Ahora()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zona);
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second, DateTimeKind.Unspecified);
    }

    public DateTime Hoy() => Ahora().Date;

    /// <summary>Lunes de la semana actual.</summary>
    public DateTime InicioSemana()
    {
        var hoy = Hoy();
        return hoy.AddDays(1 - DiaSemana(hoy));
    }

    public DateTime InicioMes()
    {
        var hoy = Hoy();
        return new DateTime(hoy.Year, hoy.Month, 1);
    }

    /// <summary>Día de la semana: 1 = lunes ... 7 = domingo.</summary>
    public static int DiaSemana(DateTime fecha) => ((int)fecha.DayOfWeek + 6) % 7 + 1;

    public static string NombreDia(int diaSemana) =>
        diaSemana is >= 1 and <= 7 ? NombresDias[diaSemana] : "";

    public static int MinutosEntre(DateTime desde, DateTime hasta) =>
        (int)Math.Max(0, Math.Round((hasta - desde).TotalMinutes));

    /// <summary>Formato de pantalla: 07/10/2026 - 12:35</summary>
    public static string Formatear(DateTime fecha) =>
        fecha.ToString("dd/MM/yyyy - HH:mm", CultureInfo.InvariantCulture);

    public static string FormatearDuracion(int minutos)
    {
        if (minutos < 60) return $"{minutos} min";
        var horas = minutos / 60;
        var resto = minutos % 60;
        if (horas < 24) return resto == 0 ? $"{horas} h" : $"{horas} h {resto} min";
        var dias = horas / 24;
        var horasResto = horas % 24;
        return horasResto == 0 ? $"{dias} d" : $"{dias} d {horasResto} h";
    }

    private static TimeZoneInfo ObtenerZona(string id)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zona)) return zona;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var idWindows) &&
            idWindows is not null &&
            TimeZoneInfo.TryFindSystemTimeZoneById(idWindows, out zona))
        {
            return zona;
        }
        if (TimeZoneInfo.TryFindSystemTimeZoneById("Montevideo Standard Time", out zona)) return zona;
        // Uruguay no aplica horario de verano desde 2015.
        return TimeZoneInfo.CreateCustomTimeZone("UY", TimeSpan.FromHours(-3), "Uruguay (UTC-3)", "Uruguay (UTC-3)");
    }
}
