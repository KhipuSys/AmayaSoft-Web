using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace MiniMarket.Web.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class TwoFactorAuthModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public TwoFactorAuthModel(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        [BindProperty]
        public DisableInputModel DisableInput { get; set; }

        public string SharedKey { get; set; }
        public string QrCodeDataUri { get; set; }
        public bool Is2faEnabled { get; set; }
        public string[] RecoveryCodes { get; set; }

        [TempData]
        public string StatusMessage { get; set; }

        public string ErrorMessage { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Ingrese el código generado por Authy.")]
            [StringLength(7, ErrorMessage = "El código debe tener 6 dígitos.", MinimumLength = 6)]
            public string Code { get; set; }
        }

        public class DisableInputModel
        {
            [StringLength(7, ErrorMessage = "El código debe tener 6 dígitos.", MinimumLength = 6)]
            public string Code { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound("No se encontró el usuario actual.");

            await LoadSharedKeyAndQrCodeUriAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostEnableAsync()
        {
            ModelState.Remove("DisableInput");
            ModelState.Remove("DisableInput.Code");
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound("No se encontró el usuario actual.");

            if (!ModelState.IsValid)
            {
                ErrorMessage = "Revisa el código ingresado e inténtalo nuevamente.";
                await LoadSharedKeyAndQrCodeUriAsync(user);
                return Page();
            }

            var verificationCode = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var is2faTokenValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

            if (!is2faTokenValid)
            {
                ErrorMessage = "No se pudo activar 2FA: el código de Authy no coincide o ya expiró. Espera el siguiente código e inténtalo otra vez.";
                ModelState.AddModelError("Input.Code", "Código inválido. Verifique el código actual en Authy.");
                await LoadSharedKeyAndQrCodeUriAsync(user);
                return Page();
            }

            await _userManager.SetTwoFactorEnabledAsync(user, true);
            await _signInManager.RefreshSignInAsync(user);
            RecoveryCodes = (await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10)).ToArray();
            StatusMessage = "Verificación en dos pasos activada correctamente.";
            await LoadSharedKeyAndQrCodeUriAsync(user);
            return Page();
        }

        public async Task<IActionResult> OnPostDisableAsync()
        {
            ModelState.Remove("Input");
            ModelState.Remove("Input.Code");
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return NotFound("No se encontró el usuario actual.");

            if (await _userManager.IsLockedOutAsync(user))
            {
                ErrorMessage = "La cuenta está bloqueada temporalmente.";
                ModelState.AddModelError(string.Empty, "La cuenta está bloqueada temporalmente.");
                await LoadSharedKeyAndQrCodeUriAsync(user);
                return Page();
            }

            var verificationCode = DisableInput?.Code?.Replace(" ", string.Empty).Replace("-", string.Empty);
            if (string.IsNullOrWhiteSpace(verificationCode))
            {
                ErrorMessage = "Ingrese el código actual de Authy para desactivar 2FA.";
                ModelState.AddModelError("DisableInput.Code", ErrorMessage);
                await LoadSharedKeyAndQrCodeUriAsync(user);
                return Page();
            }

            var is2faTokenValid = !string.IsNullOrWhiteSpace(verificationCode) && await _userManager.VerifyTwoFactorTokenAsync(
                user, _userManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

            if (!is2faTokenValid)
            {
                await _userManager.AccessFailedAsync(user);
                if (await _userManager.IsLockedOutAsync(user))
                {
                    ErrorMessage = "La cuenta quedó bloqueada temporalmente por varios intentos fallidos.";
                    ModelState.AddModelError(string.Empty, "La cuenta quedó bloqueada temporalmente por varios intentos fallidos.");
                }
                else
                {
                    ErrorMessage = "No se pudo desactivar 2FA: el código de Authy no coincide o ya expiró.";
                }
                ModelState.AddModelError("DisableInput.Code", "Código inválido. Verifique el código actual en Authy.");
                await LoadSharedKeyAndQrCodeUriAsync(user);
                return Page();
            }

            await _userManager.ResetAccessFailedCountAsync(user);
            await _userManager.SetTwoFactorEnabledAsync(user, false);
            await _userManager.ResetAuthenticatorKeyAsync(user);
            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Verificación en dos pasos desactivada.";
            return RedirectToPage();
        }

        private async Task LoadSharedKeyAndQrCodeUriAsync(IdentityUser user)
        {
            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            Is2faEnabled = await _userManager.GetTwoFactorEnabledAsync(user);
            SharedKey = Is2faEnabled ? null : FormatKey(unformattedKey);
            QrCodeDataUri = Is2faEnabled ? null : GenerateQrCodeDataUri(user.Email, unformattedKey);
        }

        private static string FormatKey(string unformattedKey)
        {
            var result = new System.Text.StringBuilder();
            int currentPosition = 0;
            while (currentPosition + 4 < unformattedKey.Length)
            {
                result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
                currentPosition += 4;
            }
            if (currentPosition < unformattedKey.Length)
                result.Append(unformattedKey.AsSpan(currentPosition));
            return result.ToString().ToLowerInvariant();
        }

        private static string GenerateQrCodeDataUri(string email, string unformattedKey)
        {
            const string issuer = "AmayaSoft";
            var authenticatorUri = string.Format("otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
                Uri.EscapeDataString(issuer),
                Uri.EscapeDataString(email),
                unformattedKey);

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(authenticatorUri, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(data).GetGraphic(8);
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }
    }
}
