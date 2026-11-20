using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Apiclase19.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<AuthController> logger) : ControllerBase
{
    private const string GitHubTokenSessionKey = "GitHubToken";
    private const string OAuthStateSessionKey = "GitHubOAuthState";
    private const string GitHubApiBaseUrl = "https://api.github.com";

    [HttpGet("login")]
    [EndpointSummary("Iniciar sesión con GitHub")]
    [ProducesResponseType(typeof(RedirectResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Login()
    {
        var clientId = configuration["GitHub:ClientId"];
        var redirectUri = configuration["GitHub:RedirectUri"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "OAuth de GitHub no está configurado",
                detail: "Configura GitHub:ClientId y GitHub:RedirectUri.");
        }

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        HttpContext.Session.SetString(OAuthStateSessionKey, state);

        var authorizationUrl = "https://github.com/login/oauth/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&scope={Uri.EscapeDataString("read:user")}"
            + $"&state={Uri.EscapeDataString(state)}";

        return Ok(Redirect(authorizationUrl));
    }

    [HttpGet("/auth/callback")]
    [EndpointSummary("Completar el inicio de sesión con GitHub")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Inicio de sesión cancelado",
                Detail = "GitHub no autorizó el acceso."
            });
        }

        var expectedState = HttpContext.Session.GetString(OAuthStateSessionKey);
        HttpContext.Session.Remove(OAuthStateSessionKey);
        if (string.IsNullOrWhiteSpace(expectedState)
            || string.IsNullOrWhiteSpace(state)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedState),
                Encoding.UTF8.GetBytes(state)))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Estado OAuth inválido",
                Detail = "Vuelve a iniciar el proceso de autenticación."
            });
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Falta el código de autorización."
            });
        }

        var clientId = configuration["GitHub:ClientId"];
        var clientSecret = configuration["GitHub:ClientSecret"];
        var redirectUri = configuration["GitHub:RedirectUri"];
        if (string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(clientSecret)
            || string.IsNullOrWhiteSpace(redirectUri))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "OAuth de GitHub no está configurado",
                detail: "Configura ClientId, ClientSecret y RedirectUri para GitHub.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClientFactory.CreateClient()
            .SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub rejected the OAuth code exchange with status {StatusCode}.",
                response.StatusCode);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "No se pudo completar la autenticación con GitHub.");
        }

        GitHubTokenResponse? tokenResponse;
        try
        {
            tokenResponse = await response.Content.ReadFromJsonAsync<GitHubTokenResponse>(
                cancellationToken);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "GitHub returned an invalid OAuth token response.");
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "GitHub devolvió una respuesta de autenticación inválida.");
        }

        if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "GitHub no devolvió un token de acceso.");
        }

        using var userResponse = await RequestGitHubUserAsync(
            tokenResponse.AccessToken,
            cancellationToken);
        if (!userResponse.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub profile lookup failed after OAuth with status {StatusCode}.",
                userResponse.StatusCode);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "No se pudo verificar la cuenta de GitHub.");
        }

        var user = await ReadGitHubUserAsync(userResponse, cancellationToken);
        if (user is null)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "GitHub devolvió un perfil inválido.");
        }

        HttpContext.Session.SetString(GitHubTokenSessionKey, tokenResponse.AccessToken);
        return Ok("Usuario autenticado correctamente");
    }

    [HttpGet("EstaAutenticado")]
    [EndpointSummary("Verificar la sesión y el token de GitHub")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> VerifyAuthentication(CancellationToken cancellationToken)
    {
        var token = HttpContext.Session.GetString(GitHubTokenSessionKey);
        if (token is null)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "No autenticado",
                Detail = "Inicia sesión con GitHub primero."
            });
        }

        using var response = await RequestGitHubUserAsync(token, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            HttpContext.Session.Remove(GitHubTokenSessionKey);
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Token de GitHub inválido o revocado",
                Detail = "Vuelve a iniciar sesión con GitHub."
            });
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub profile verification failed with status {StatusCode}.",
                response.StatusCode);
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "No se pudo verificar la sesión con GitHub.");
        }

        var user = await ReadGitHubUserAsync(response, cancellationToken);
        if (user is null)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "GitHub devolvió un perfil inválido.");
        }

        return Ok(new { user.Id, user.Login, user.Name });
    }

    [HttpPost("logout")]
    [EndpointSummary("Cerrar la sesión de GitHub")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        HttpContext.Session.Remove(GitHubTokenSessionKey);
        HttpContext.Session.Remove(OAuthStateSessionKey);
        return NoContent();
    }

    private async Task<HttpResponseMessage> RequestGitHubUserAsync(
        string token,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{GitHubApiBaseUrl}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("Apiclase19-OAuth");

        return await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
    }

    private static async Task<GitHubUserResponse?> ReadGitHubUserAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<GitHubUserResponse>(
                cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record GitHubTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record GitHubUserResponse(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("login")] string Login,
        [property: JsonPropertyName("name")] string? Name);
}
