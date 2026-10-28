using Desarrollo.Domain;
using Desarrollo.Domain.Organizations;

namespace Desarrollo.Web.Seed;

public static class SystemRoles
{
    public static readonly (string Name, string Description, Permission[] Permissions)[] Definitions =
    {
        ("Administrador de organización", "Gestiona políticas y acceso global.", Enum.GetValues<Permission>()),
        ("Líder de proyecto", "Organiza el trabajo del proyecto.", new[]
        {
            Permission.Leer, Permission.Comentar, Permission.Crear, Permission.Editar, Permission.Asignar,
            Permission.CambiarEstado, Permission.Aprobar, Permission.Exportar, Permission.Archivar,
            Permission.AdministrarMiembros, Permission.ConfigurarFlujo,
        }),
        ("Responsable de producto", "Define requisitos y aceptación.", new[]
        {
            Permission.Leer, Permission.Comentar, Permission.Crear, Permission.Editar, Permission.Asignar,
            Permission.CambiarEstado, Permission.Aprobar, Permission.Exportar,
        }),
        ("Desarrollador o diseñador", "Ejecuta el trabajo.", new[]
        {
            Permission.Leer, Permission.Comentar, Permission.Crear, Permission.Editar, Permission.CambiarEstado,
        }),
        ("QA o revisor", "Valida la calidad.", new[]
        {
            Permission.Leer, Permission.Comentar, Permission.Crear, Permission.Editar, Permission.CambiarEstado, Permission.Aprobar,
        }),
        ("Cliente o invitado", "Accede a recursos compartidos.", new[] { Permission.Leer, Permission.Comentar }),
        ("Lector", "Solo lectura.", new[] { Permission.Leer }),
    };
}
