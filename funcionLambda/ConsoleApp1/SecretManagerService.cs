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
                string secretName = $"/catalog-api/{env}/tenants/{tenantId}/spapi";

                ctx.Logger.LogLine($"Secret Query: {secretName}");

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

        public async Task<MiraviaSecret?> GetMiraviaSecretAsync(string tenantId, string env, string projectName, ILambdaContext ctx)
        {
            try
            {
                string secretName = $"/catalog-api/{env}/tenants/{tenantId}/miravia";

                ctx.Logger.LogLine($"Miravia Secret Query: {secretName}");

                var response = await _secretsManager.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = secretName });

                var secret = JsonSerializer.Deserialize<MiraviaSecret>(response.SecretString);

                ctx.Logger.LogLine($"Miravia AppKey: {secret?.AppKey ?? ""}");

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

        public async Task<PcComponentesSecret?> GetPcComponentesSecretAsync(string tenantId, string env, string projectName, ILambdaContext ctx)
        {
            try
            {
                string secretName = $"/catalog-api/{env}/tenants/{tenantId}/pccomponentes";

                ctx.Logger.LogLine($"PcComponentes Secret Query: {secretName}");

                var response = await _secretsManager.GetSecretValueAsync(
                    new GetSecretValueRequest { SecretId = secretName });

                var secret = JsonSerializer.Deserialize<PcComponentesSecret>(response.SecretString);

                ctx.Logger.LogLine($"PcComponentes ApiKey length: {secret?.ApiKey?.Length ?? 0}");

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
