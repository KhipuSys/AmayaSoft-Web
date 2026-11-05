using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MiniMarket.Web.Areas.Identity.Pages.Account.Manage
{
    [Authorize]
    public class EnableAuthenticatorModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("./TwoFactorAuth");

        public IActionResult OnPost() => RedirectToPage("./TwoFactorAuth");
    }
}
