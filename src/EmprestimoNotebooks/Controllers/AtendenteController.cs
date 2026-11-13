using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EmprestimoNotebooks.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EmprestimoNotebooks.Controllers;

public class AtendenteController(IConfiguration configuracao) : Controller
{
    [AllowAnonymous]
    [HttpGet("/entrar")]
    public IActionResult Entrar(string? retorno)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Notebooks");
        }

        return View(new EntrarFormulario { Retorno = retorno });
    }

    [AllowAnonymous]
    [HttpPost("/entrar")]
    public async Task<IActionResult> Entrar([FromForm] EntrarFormulario formulario)
    {
        if (!ModelState.IsValid)
        {
            return View(formulario);
        }

        var usuario = configuracao["Atendente:Usuario"];
        var senha = configuracao["Atendente:Senha"];

        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(senha)
            || !string.Equals(usuario, formulario.Usuario, StringComparison.Ordinal)
            || !SenhasConferem(senha, formulario.Senha))
        {
            ModelState.AddModelError(string.Empty, "Usuário ou senha inválidos.");
            return View(formulario);
        }

        var identidade = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, usuario)], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(new ClaimsPrincipal(identidade));

        return Url.IsLocalUrl(formulario.Retorno)
            ? LocalRedirect(formulario.Retorno)
            : RedirectToAction("Index", "Notebooks");
    }

    [HttpPost("/sair")]
    public async Task<IActionResult> Sair()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction(nameof(Entrar));
    }

    private static bool SenhasConferem(string senhaConfigurada, string senhaInformada)
    {
        var hashConfigurado = SHA256.HashData(Encoding.UTF8.GetBytes(senhaConfigurada));
        var hashInformado = SHA256.HashData(Encoding.UTF8.GetBytes(senhaInformada));
        return CryptographicOperations.FixedTimeEquals(hashConfigurado, hashInformado);
    }
}
