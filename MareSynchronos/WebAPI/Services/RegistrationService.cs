using MareSynchronos.API.Dto;
using MareSynchronos.API.Routes;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.Utils;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;

namespace MareSynchronos.WebAPI.Services
{
    public sealed class RegistrationService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RegistrationService> _logger;
        private readonly ServerConfigurationManager _serverManager;

        private static string GenerateSecretKey()
        {
            return Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(64)));
        }

        public RegistrationService(ILogger<RegistrationService> logger, ServerConfigurationManager serverManager)
        {
            _logger = logger;
            _serverManager = serverManager;
            _httpClient = new(
                new HttpClientHandler
                {
                    AllowAutoRedirect = true,
                    MaxAutomaticRedirections = 5
                }
            );
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MareSynchronos", ver!.Major + "." + ver!.Minor + "." + ver!.Build));
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        public async Task<RegistrationDto> RegisterAccount(CancellationToken token)
        {
            var authApiUrl = _serverManager.CurrentApiUrl;

            _logger.LogInformation("Registrating on: {server}", authApiUrl);

            var secretKey = GenerateSecretKey();
            var hashedSecretKey = secretKey.GetHash256();

            return await RegisterNewAccount(authApiUrl, secretKey, hashedSecretKey, false, token).ConfigureAwait(false);
        }

        private async Task<RegistrationDto> RegisterNewAccount(string authApiUrl, string secretKey, string hashedSecretKey, bool useV2, CancellationToken token)
        {
            Uri postUri = MareAuth.AuthRegisterFullPath(new Uri(authApiUrl
                            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));

            if(useV2)
            {
                postUri = MareAuth.AuthRegisterV2FullPath(new Uri(authApiUrl
                            .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                            .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));
            }

            var result = await _httpClient.PostAsync(postUri, new FormUrlEncodedContent([
                new("hashedSecretKey", hashedSecretKey)
            ]), token).ConfigureAwait(false);

            if (result.IsSuccessStatusCode)
            {
                var response = await result.Content.ReadFromJsonAsync<RegistrationDto>(token).ConfigureAwait(false) ?? new();
                response.SecretKey = secretKey;

                return response;
            }
            else if(!useV2)
            {
                //Try again with V2
                return await RegisterNewAccount(authApiUrl, secretKey, hashedSecretKey, true, token).ConfigureAwait(false);
            }
            else
            {
                //If failed twice, log the error and leave.
                var error = await result.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                _logger.LogError("Failed to register on server {server}. Status code: {statusCode}. Error: {error}", authApiUrl, result.StatusCode, error);
                throw new HttpRequestException($"Failed to register on server {authApiUrl}. Status code: {result.StatusCode}. Error: {error}");
            }
        }

    }
}
