using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace proyecto.api.Errors;

public sealed class MlAgentAuthorizationFilter(IConfiguration configuration) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = configuration["Ml:AgentKey"];
        var received = context.HttpContext.Request.Headers["X-ML-Agent-Key"].ToString();

        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32 || string.IsNullOrEmpty(received) ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)), SHA256.HashData(Encoding.UTF8.GetBytes(received))))
            context.Result = new UnauthorizedObjectResult(new { mensaje = "Credencial de agente ausente o incorrecta." });
    }
}
