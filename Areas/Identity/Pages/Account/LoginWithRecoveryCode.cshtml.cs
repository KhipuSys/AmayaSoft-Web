using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MiniMarket.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class LoginWithRecoveryCodeModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;

        public LoginWithRecoveryCodeModel(SignInManager<IdentityUser> signInManager)
        {
            _signInManager = signInManager;
        }

        [BindProperty]
        public InputModel Input { get; set; }

        public string ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Ingrese un código de recuperación.")]
            [DataType(DataType.Text)]
            public string RecoveryCode { get; set; }
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

            var recoveryCode = Input.RecoveryCode.Replace(" ", string.Empty);
            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(recoveryCode);
            if (result.Succeeded)
                return LocalRedirect(returnUrl);

            ModelState.AddModelError(string.Empty, "Código de recuperación inválido.");
            ReturnUrl = returnUrl;
            return Page();
        }

        private string NormalizeReturnUrl(string returnUrl) =>
            string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) ? Url.Content("~/") : returnUrl;
    }
}
