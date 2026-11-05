using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MiniMarket.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWith2faModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginWith2faModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Ingrese el código de Authy.")]
            [StringLength(7, ErrorMessage = "El código debe tener 6 dígitos.", MinimumLength = 6)]
            [DataType(DataType.Text)]
            public string TwoFactorCode { get; set; }
        }

        public async Task<IActionResult> OnGetAsync(string returnUrl = null)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
                return RedirectToPage("./Login");

            ReturnUrl = NormalizeReturnUrl(returnUrl);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl = NormalizeReturnUrl(returnUrl);
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
                return RedirectToPage("./Login");

            if (!ModelState.IsValid)
            {
                ReturnUrl = returnUrl;
                return Page();
            }

            var code = Input.TwoFactorCode.Replace(" ", string.Empty).Replace("-", string.Empty);
            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);

            if (result.Succeeded)
                return LocalRedirect(returnUrl);

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "La cuenta está bloqueada temporalmente por varios intentos fallidos.");
                ReturnUrl = returnUrl;
                return Page();
            }

            ModelState.AddModelError(string.Empty, "Código inválido. Verifique el código actual en Authy.");
            ReturnUrl = returnUrl;
            return Page();
        }

        private string NormalizeReturnUrl(string returnUrl) =>
            string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? Url.Content("~/") : returnUrl;
    }
}
