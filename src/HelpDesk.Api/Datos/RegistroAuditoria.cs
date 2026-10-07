namespace HelpDesk.Api.Datos;

/// <summary>Registra acciones administrativas en dbo.Auditoria.</summary>
public static class RegistroAuditoria
{
    public static Task RegistrarAsync(
        IDbConnection cn,
        IDbTransaction? tx,
        int? usuarioId,
        DateTime fecha,
        string entidad,
        string? entidadId,
        string accion,
        object? detalle = null)
    {
        return cn.ExecuteAsync(
            """
            INSERT INTO dbo.Auditoria (FechaHora, UsuarioId, Entidad, EntidadId, Accion, Detalle)
            VALUES (@fecha, @usuarioId, @entidad, @entidadId, @accion, @detalle)
            """,
            new
            {
                fecha,
                usuarioId,
                entidad,
                entidadId,
                accion,
                detalle = detalle is null ? null : JsonSerializer.Serialize(detalle)
            },
            tx);
    }
}
