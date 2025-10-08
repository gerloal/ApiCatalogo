using Amazon.Lambda.Core;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuncionLambda
{
    public class SecretManagerService
    {
        private readonly IAmazonSecretsManager _secretsManager;

        public SecretManagerService(IAmazonSecretsManager secretsManager)
        {
            _secretsManager = secretsManager;
        }

        public async Task<SpApiSecret?> GetSpApiSecretAsync(string tenantId, string env, string projectName, ILambdaContext ctx)
        {
            try
            {

                // Nombre del secreto según cómo lo definimos en Terraform:
                // /<project>/<env>/tenants/<tenantId>/spapi
                string secretName = $"/{projectName}/{env}/tenants/{tenantId}/spapi";

                var response = await _secretsManager.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = secretName });



                var secret = JsonSerializer.Deserialize<SpApiSecret>(response.SecretString);

                ctx.Logger.LogLine($"ClientId: {secret.ClientId ?? ""}");
                ctx.Logger.LogLine($"RoleArn: {secret.RoleArn ?? ""}");

                return secret;
            }
            catch (SocketException ex)
            {
                ctx.Logger.LogLine($"SocketException: {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                ctx.Logger.LogLine($"Exception: {ex.Message}");
                throw;
            }
        }

    }
   
}
