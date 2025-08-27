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

        public async Task<RegistrationDto> RegisterAccount(string serverUri, CancellationToken token)
        {
            var authApiUrl = _serverManager.CurrentApiUrl ?? serverUri;

            _logger.LogInformation("Registrating on: {server}", serverUri);

            var secretKey = GenerateSecretKey();
            var hashedSecretKey = secretKey.GetHash256();

            Uri postUri = MareAuth.RenewOAuthTokenFullPath(new Uri(authApiUrl
                .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase)
                .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)));

            var result = await _httpClient.PostAsync(postUri, new FormUrlEncodedContent([
                new("hashedSecretKey", hashedSecretKey)
            ]), token).ConfigureAwait(false);

            if (result.IsSuccessStatusCode)
            { 
                var response = await result.Content.ReadFromJsonAsync<RegistrationDto>(token).ConfigureAwait(false) ?? new();
                response.SecretKey = secretKey;

                return response;
            }
            else
            {
                var error = await result.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                _logger.LogError("Failed to register on server {server}. Status code: {statusCode}. Error: {error}", serverUri, result.StatusCode, error);
                throw new HttpRequestException($"Failed to register on server {serverUri}. Status code: {result.StatusCode}. Error: {error}");
            }
        }
    }
}
