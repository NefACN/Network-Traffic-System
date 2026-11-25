namespace proyect.domain.Constants
{
    public static class RolesSistemas
    {
        public const string RolAdmin = "Admin";
        public const string RolGerente = "GerenteSucursal";
        public const string RolColaborador = "Colaborador";
        public const string RolesPreventivos = RolAdmin + "," + RolGerente;
        public const string RolesAprendizaje = RolAdmin + "," + RolColaborador;

        public static bool EsActor(string nombre) => nombre is RolAdmin or RolGerente or RolColaborador;

        // los roles antiguos sin permisos administrativos conservan el portal de aprendizaje
        public static string ActorEfectivo(IEnumerable<string> roles) => roles.Contains(RolAdmin) ? RolAdmin
            : roles.Contains(RolGerente) ? RolGerente : RolColaborador;
    }
}
