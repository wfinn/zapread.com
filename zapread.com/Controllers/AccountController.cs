﻿using System;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.Owin;
using Microsoft.Owin.Security;
using zapread.com.Models;
using zapread.com.Helpers;
using Jdenticon;
using Jdenticon.Rendering;
using System.IO;
using zapread.com.Database;
using System.Drawing;
using System.Drawing.Imaging;
using System.Data.Entity;
using Microsoft.Owin.Host.SystemWeb;
using zapread.com.Services;

namespace zapread.com.Controllers
{
    /// <summary>
    /// This controller handles user account aspects related to identity
    /// </summary>
    [Authorize]
    [RoutePrefix("Account")]
    public class AccountController : Controller
    {
        private ApplicationSignInManager _signInManager;
        private ApplicationUserManager _userManager;
        private ApplicationRoleManager _roleManager;

        public AccountController()
        {
        }

        public AccountController(ApplicationUserManager userManager, ApplicationSignInManager signInManager )
        {
            UserManager = userManager;
            SignInManager = signInManager;
        }

        public AccountController( 
            ApplicationUserManager userManager, 
            ApplicationSignInManager signInManager, 
            ApplicationRoleManager roleManager) 
        { 
            this.RoleManager = roleManager; 
            this.SignInManager = signInManager; 
            this.UserManager = userManager; 
        }

        /* Monetary aspects */

        public async Task<JsonResult> UserBalance()
        {
            var userId = User.Identity.GetUserId();

            if (userId == null)
            {
                return Json(new { balance = 0 });
            }

            using (var db = new ZapContext())
            {
                await EnsureUserExists(userId, db);
                var user = db.Users
                    .Include(usr => usr.Funds)
                    .FirstOrDefault(u => u.AppId == userId);

                return Json(new { balance = Math.Floor(user.Funds.Balance) });
            }
        }

        private async Task EnsureUserExists(string userId, ZapContext db)
        {
            if (userId != null)
            {
                if (db.Users.Where(u => u.AppId == userId).Count() == 0)
                {
                    // no user entry
                    User u = new Models.User()
                    {
                        AboutMe = "Nothing to tell.",
                        AppId = userId,
                        Name = UserManager.FindById(userId).UserName,
                        ProfileImage = new UserImage(),
                        ThumbImage = new UserImage(),
                        Funds = new UserFunds(),
                        Settings = new UserSettings(),
                        DateJoined = DateTime.UtcNow,
                    };
                    db.Users.Add(u);
                    await db.SaveChangesAsync();
                }
            }
        }

        public ActionResult Balance()
        {
            ViewBag.Balance = GetUserBalance();
            return PartialView("_PartialBalance");
        }

        //
        // GET: /Account/GetBalance
        // Returns the currently logged in user balance
        [HttpGet]
        [AllowAnonymous]
        public ActionResult GetBalance()
        {
            double userBalance = 0.0;
            if (Request.IsAuthenticated)
            {
                userBalance = GetUserBalance();
            }
            string balance = userBalance.ToString("0.##");
            return Json(new { balance }, JsonRequestBehavior.AllowGet);
        }

        private double GetUserBalance()
        {
            double balance;
            try
            {
                using (var db = new ZapContext())
                {
                    // Get the logged in user ID
                    var uid = User.Identity.GetUserId();
                    var user = db.Users
                        .Include(i => i.Funds)
                        .AsNoTracking().FirstOrDefault(u => u.AppId == uid);

                    if (user == null)
                    {
                        // User not found in database, or not logged in
                        balance = 0.0;
                    }
                    else
                    {
                        if (user.Funds == null)
                        {
                            // Neets to be initialized
                            var user_modified = db.Users
                                .Include(i => i.Funds)
                                .FirstOrDefault(u => u.AppId == uid);

                            user_modified.Funds = new UserFunds() { Balance = 0.0, Id = user_modified.Id, TotalEarned = 0.0 };
                            db.SaveChanges();
                            user = user_modified;
                        }
                        balance = user.Funds.Balance;
                    }
                }
            }
            catch (Exception)
            {
                // todo: add some error logging

                // If we have an exception, it is possible a user is trying to abuse the system.  Return 0 to be uninformative.
                balance = 0.0;
            }

            return Math.Floor(balance);
        }

        [HttpGet]
        [Route("GetSpendingSum/{days?}")]
        public ActionResult GetSpendingSum(string days)
        {
            double amount = 0.0;
            int numDays = Convert.ToInt32(days);
            double totalAmount = 0.0;

            // Get the logged in user ID
            var uid = User.Identity.GetUserId();

            try
            {
                using (var db = new ZapContext())
                {
                    var userTxns = db.Users
                            .Include(i => i.SpendingEvents)
                            .Where(u => u.AppId == uid)
                            .SelectMany(u => u.SpendingEvents);

                    var sum = userTxns
                        .Where(tx => DbFunctions.DiffDays(tx.TimeStamp, DateTime.Now) <= numDays)   // Filter for time
                        .Sum(tx => tx.Amount);

                    totalAmount = userTxns
                        .Sum(tx => tx.Amount);

                    amount = sum;
                }
            }
            catch (Exception e)
            {
                MailingService.Send(new UserEmailModel()
                {
                    Destination = "steven.horn.mail@gmail.com",
                    Body = " Exception: " + e.Message + "\r\n Stack: " + e.StackTrace + "\r\n method: GetSpendingSum" + "\r\n user: " + uid,
                    Email = "",
                    Name = "zapread.com Exception",
                    Subject = "Account Controller error",
                });

                // If we have an exception, it is possible a user is trying to abuse the system.  Return 0 to be uninformative.
                amount = 0.0;
            }

            string value = amount.ToString("0.##");
            string total = totalAmount.ToString("0.##");
            return Json(new { value, total }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("GetEarningsSum/{days?}")]
        public ActionResult GetEarningsSum(string days)
        {
            double amount = 0.0;
            int numDays = Convert.ToInt32(days);
            double totalAmount = 0.0;
            try
            {
                using (var db = new ZapContext())
                {
                    // Get the logged in user ID
                    var uid = User.Identity.GetUserId();
                    var userTxns = db.Users
                            .Include(i => i.EarningEvents)
                            .Where(u => u.AppId == uid)
                            .SelectMany(u => u.EarningEvents);

                    var sum = userTxns
                        .Where(tx => DbFunctions.DiffDays(tx.TimeStamp, DateTime.Now) <= numDays)   // Filter for time
                        .Sum(tx => tx.Amount);
                    

                    totalAmount = userTxns
                        .Sum(tx => tx.Amount);

                    amount = sum;
                }
            }
            catch (Exception e)
            {
                // todo: add some error logging

                // If we have an exception, it is possible a user is trying to abuse the system.  Return 0 to be uninformative.
                amount = 0.0;
            }

            string value = amount.ToString("0.##");
            string total = totalAmount.ToString("0.##");
            return Json(new { value, total }, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Route("GetLNFlow/{days?}")]
        public ActionResult GetLNFlow(string days)
        {
            double amount = 0.0;
            int numDays = Convert.ToInt32(days);
            try
            {
                using (var db = new ZapContext())
                {
                    // Get the logged in user ID
                    var uid = User.Identity.GetUserId();
                    var userTxns = db.Users
                            .Include(i => i.LNTransactions)
                            .Where(u => u.AppId == uid)
                            .SelectMany(u => u.LNTransactions);

                    var sum = userTxns
                        .Where(tx => DbFunctions.DiffDays(tx.TimestampSettled, DateTime.Now) <= numDays)   // Filter for time
                        .Select(tx => new { amt = tx.IsDeposit ? tx.Amount : -1.0*tx.Amount })
                        .Sum(tx => tx.amt);
                        
                    amount = sum;
                }
            }
            catch (Exception e)
            {
                // todo: add some error logging

                // If we have an exception, it is possible a user is trying to abuse the system.  Return 0 to be uninformative.
                amount = 0.0;
            }
            string value = amount.ToString("0.##");
            return Json(new { value }, JsonRequestBehavior.AllowGet);
        }

        /* Identity aspects*/

        public ApplicationSignInManager SignInManager
        {
            get
            {
                return _signInManager ?? HttpContext.GetOwinContext().Get<ApplicationSignInManager>();
            }
            private set 
            { 
                _signInManager = value; 
            }
        }

        public ApplicationUserManager UserManager
        {
            get
            {
                return _userManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationUserManager>();
            }
            private set
            {
                _userManager = value;
            }
        }

        public ApplicationRoleManager RoleManager
        {
            get
            {
                return _roleManager ?? HttpContext.GetOwinContext().GetUserManager<ApplicationRoleManager>();
            }
            private set
            {
                _roleManager = value;
            }
        }

        //
        // GET: /Account/Login
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        //
        // POST: /Account/Login
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Login(LoginViewModel model, string returnUrl)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Add administrator user impersonation code here (for debug)

            ////string userNameToImpersonate = "THEUSERNAMEHERE";

            ////var userToImpersonate = await UserManager
            ////    .FindByNameAsync(userNameToImpersonate);
            ////var identityToImpersonate = await UserManager
            ////    .CreateIdentityAsync(userToImpersonate,
            ////        DefaultAuthenticationTypes.ApplicationCookie);
            ////var authenticationManager = HttpContext.GetOwinContext().Authentication;
            ////authenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
            ////authenticationManager.SignIn(new AuthenticationProperties()
            ////{
            ////    IsPersistent = false
            ////}, identityToImpersonate);
            ////var result = SignInStatus.Success;

            // This doesn't count login failures towards account lockout
            // To enable password failures to trigger account lockout, change to shouldLockout: true
            var result = await SignInManager.PasswordSignInAsync(
                userName: model.UserName,
                password: model.Password,
                isPersistent: model.RememberMe,
                shouldLockout: false);

            switch (result)
            {
                case SignInStatus.Success:
                    {
                        var userId = await UserManager.FindByNameAsync(model.UserName);//User.Identity.GetUserId();
                        using (var db = new ZapContext())
                        {
                            await EnsureUserExists(userId.Id, db);

                            // Apply claims
                            var u = db.Users
                                .Where(us => us.AppId == userId.Id).First();

                            //await UserManager.AddClaimAsync(userId.Id, new Claim("ColorTheme", u.Settings.ColorTheme));

                            var identity = await UserManager
                                .CreateIdentityAsync(userId,
                                DefaultAuthenticationTypes.ApplicationCookie);

                            identity.AddClaim(new Claim("ColorTheme", u.Settings.ColorTheme ?? "light"));

                            try
                            {
                                var authenticationManager = HttpContext.GetOwinContext().Authentication;
                                authenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
                                authenticationManager.SignIn(new AuthenticationProperties()
                                {
                                    IsPersistent = model.RememberMe
                                }, identity);
                            }
                            catch(Exception)
                            {
                                // Need to better handle this
                            }
                        }
                        return RedirectToLocal(returnUrl);
                    }
                    
                case SignInStatus.LockedOut:
                    return View("Lockout");
                case SignInStatus.RequiresVerification:
                    return RedirectToAction("SendCode", new { ReturnUrl = returnUrl, RememberMe = model.RememberMe });
                case SignInStatus.Failure:
                default:
                    ModelState.AddModelError("", "Invalid login attempt.");
                    return View(model);
            }
        }

        //
        // GET: /Account/VerifyCode
        [AllowAnonymous]
        public async Task<ActionResult> VerifyCode(string provider, string returnUrl, bool rememberMe)
        {
            // Require that the user has already logged in via username/password or external login
            if (!await SignInManager.HasBeenVerifiedAsync())
            {
                return View("Error");
            }
            return View(new VerifyCodeViewModel { Provider = provider, ReturnUrl = returnUrl, RememberMe = rememberMe });
        }

        //
        // POST: /Account/VerifyCode
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> VerifyCode(VerifyCodeViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // The following code protects for brute force attacks against the two factor codes. 
            // If a user enters incorrect codes for a specified amount of time then the user account 
            // will be locked out for a specified amount of time. 
            // You can configure the account lockout settings in IdentityConfig
            var result = await SignInManager.TwoFactorSignInAsync(model.Provider, model.Code, isPersistent:  model.RememberMe, rememberBrowser: model.RememberBrowser);
            switch (result)
            {
                case SignInStatus.Success:
                    return RedirectToLocal(model.ReturnUrl);
                case SignInStatus.LockedOut:
                    return View("Lockout");
                case SignInStatus.Failure:
                default:
                    ModelState.AddModelError("", "Invalid code.");
                    return View(model);
            }
        }

        //
        // GET: /Account/Register
        [AllowAnonymous]
        public ActionResult Register()
        {
            return View();
        }

        //
        // POST: /Account/Register
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Register(RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = new ApplicationUser { UserName = model.UserName, Email = model.Email };
                var result = await UserManager.CreateAsync(user, model.Password);
                if (result.Succeeded)
                {
                    await SignInManager.SignInAsync(user, isPersistent:false, rememberBrowser:false);
                    
                    // For more information on how to enable account confirmation and password reset please visit https://go.microsoft.com/fwlink/?LinkID=320771
                    // Send an email with this link
                    string code = await UserManager.GenerateEmailConfirmationTokenAsync(user.Id);
                    var callbackUrl = Url.Action("ConfirmEmail", "Account", new { userId = user.Id, code = code }, protocol: Request.Url.Scheme);
                    await UserManager.SendEmailAsync(user.Id, "Confirm your account", "Please confirm your account by clicking or navigating to the following link: <a href=\"" + callbackUrl + "\">"+ callbackUrl + "</a>");

                    // Initialize ZapRead user with default parameters
                    var userId = await UserManager.FindByNameAsync(model.UserName);
                    using (var db = new ZapContext())
                    {
                        await EnsureUserExists(userId.Id, db);
                    }

                    return RedirectToAction("Index", "Home");
                }
                AddErrors(result);
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // GET: /Account/ConfirmEmail
        [AllowAnonymous]
        public async Task<ActionResult> ConfirmEmail(string userId, string code)
        {
            if (userId == null || code == null)
            {
                return View("Error");
            }
            var result = await UserManager.ConfirmEmailAsync(userId, code);
            return View(result.Succeeded ? "ConfirmEmail" : "Error");
        }

        //
        // GET: /Account/ForgotPassword
        [AllowAnonymous]
        public ActionResult ForgotPassword()
        {
            return View();
        }

        //
        // POST: /Account/ForgotPassword
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (ModelState.IsValid)
            {
                var user = await UserManager.FindByEmailAsync(model.Email);
                if (user == null)// || !(await UserManager.IsEmailConfirmedAsync(user.Id)))
                {
                    // Don't reveal that the user does not exist or is not confirmed
                    return View("ForgotPasswordConfirmation");
                }

                // For more information on how to enable account confirmation and password reset please visit https://go.microsoft.com/fwlink/?LinkID=320771
                // Send an email with this link
                string code = await UserManager.GeneratePasswordResetTokenAsync(user.Id);
                var callbackUrl = Url.Action("ResetPassword", "Account", new { userId = user.Id, code = code }, protocol: Request.Url.Scheme);		
                await UserManager.SendEmailAsync(user.Id, "Reset Password", "Please reset your password by clicking <a href=\"" + callbackUrl + "\">here</a>, or pasting the following address into your browser: " + callbackUrl);
                return RedirectToAction("ForgotPasswordConfirmation", "Account");
            }

            // If we got this far, something failed, redisplay form
            return View(model);
        }

        //
        // GET: /Account/ForgotPasswordConfirmation
        [AllowAnonymous]
        public ActionResult ForgotPasswordConfirmation()
        {
            return View();
        }

        //
        // GET: /Account/ResetPassword
        [AllowAnonymous]
        public ActionResult ResetPassword(string code)
        {
            return code == null ? View("Error") : View();
        }

        //
        // POST: /Account/ResetPassword
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }
            var user = await UserManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                // Don't reveal that the user does not exist
                return RedirectToAction("ResetPasswordConfirmation", "Account");
            }
            var result = await UserManager.ResetPasswordAsync(user.Id, model.Code, model.Password);
            if (result.Succeeded)
            {
                return RedirectToAction("ResetPasswordConfirmation", "Account");
            }
            AddErrors(result);
            return View();
        }

        //
        // GET: /Account/ResetPasswordConfirmation
        [AllowAnonymous]
        public ActionResult ResetPasswordConfirmation()
        {
            return View();
        }

        //
        // POST: /Account/ExternalLogin
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult ExternalLogin(string provider, string returnUrl)
        {
            // Request a redirect to the external login provider
            return new ChallengeResult(provider, Url.Action("ExternalLoginCallback", "Account", new { ReturnUrl = returnUrl }));
        }

        //
        // GET: /Account/SendCode
        [AllowAnonymous]
        public async Task<ActionResult> SendCode(string returnUrl, bool rememberMe)
        {
            var userId = await SignInManager.GetVerifiedUserIdAsync();
            if (userId == null)
            {
                return View("Error");
            }
            var userFactors = await UserManager.GetValidTwoFactorProvidersAsync(userId);
            var factorOptions = userFactors.Select(purpose => new SelectListItem { Text = purpose, Value = purpose }).ToList();
            return View(new SendCodeViewModel { Providers = factorOptions, ReturnUrl = returnUrl, RememberMe = rememberMe });
        }

        //
        // POST: /Account/SendCode
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SendCode(SendCodeViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View();
            }

            // Generate the token and send it
            if (!await SignInManager.SendTwoFactorCodeAsync(model.SelectedProvider))
            {
                return View("Error");
            }
            return RedirectToAction("VerifyCode", new { Provider = model.SelectedProvider, ReturnUrl = model.ReturnUrl, RememberMe = model.RememberMe });
        }

        //
        // GET: /Account/ExternalLoginCallback
        [AllowAnonymous]
        public async Task<ActionResult> ExternalLoginCallback(string returnUrl)
        {
            var loginInfo = await AuthenticationManager.GetExternalLoginInfoAsync();
            if (loginInfo == null)
            {
                return RedirectToAction("Login");
            }

            // Sign in the user with this external login provider if the user already has a login
            var result = await SignInManager.ExternalSignInAsync(loginInfo, isPersistent: false);
            switch (result)
            {
                case SignInStatus.Success:
                    return RedirectToLocal(returnUrl);
                case SignInStatus.LockedOut:
                    return View("Lockout");
                case SignInStatus.RequiresVerification:
                    return RedirectToAction("SendCode", new { ReturnUrl = returnUrl, RememberMe = false });
                case SignInStatus.Failure:
                default:
                    // If the user does not have an account, then prompt the user to create an account
                    ViewBag.ReturnUrl = returnUrl;
                    ViewBag.LoginProvider = loginInfo.Login.LoginProvider;
                    return View("ExternalLoginConfirmation", new ExternalLoginConfirmationViewModel { Email = loginInfo.Email });
            }
        }

        //
        // POST: /Account/ExternalLoginConfirmation
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ExternalLoginConfirmation(ExternalLoginConfirmationViewModel model, string returnUrl)
        {
            if (User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Manage");
            }

            if (ModelState.IsValid)
            {
                // Get the information about the user from the external login provider
                var info = await AuthenticationManager.GetExternalLoginInfoAsync();
                if (info == null)
                {
                    return View("ExternalLoginFailure");
                }
                var user = new ApplicationUser { UserName = model.UserName, Email = model.Email };
                var result = await UserManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    result = await UserManager.AddLoginAsync(user.Id, info.Login);
                    if (result.Succeeded)
                    {
                        await SignInManager.SignInAsync(user, isPersistent: false, rememberBrowser: false);
                        return RedirectToLocal(returnUrl);
                    }
                }
                AddErrors(result);
            }

            ViewBag.ReturnUrl = returnUrl;
            return View(model);
        }

        //
        // POST: /Account/LogOff
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LogOff()
        {
            AuthenticationManager.SignOut(DefaultAuthenticationTypes.ApplicationCookie);
            return RedirectToAction("Index", "Home");
        }

        //
        // GET: /Account/ExternalLoginFailure
        [AllowAnonymous]
        public ActionResult ExternalLoginFailure()
        {
            return View();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_userManager != null)
                {
                    _userManager.Dispose();
                    _userManager = null;
                }

                if (_signInManager != null)
                {
                    _signInManager.Dispose();
                    _signInManager = null;
                }
            }

            base.Dispose(disposing);
        }

        #region Helpers
        // Used for XSRF protection when adding external logins
        private const string XsrfKey = "XsrfId";

        private IAuthenticationManager AuthenticationManager
        {
            get
            {
                return HttpContext.GetOwinContext().Authentication;
            }
        }

        private void AddErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("", error);
            }
        }

        private ActionResult RedirectToLocal(string returnUrl)
        {
            if (Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // Add parameter l = 1 to invalidate cache after login
            return RedirectToAction("Index", "Home", new { l = 1 });
        }

        internal class ChallengeResult : HttpUnauthorizedResult
        {
            public ChallengeResult(string provider, string redirectUri)
                : this(provider, redirectUri, null)
            {
            }

            public ChallengeResult(string provider, string redirectUri, string userId)
            {
                LoginProvider = provider;
                RedirectUri = redirectUri;
                UserId = userId;
            }

            public string LoginProvider { get; set; }
            public string RedirectUri { get; set; }
            public string UserId { get; set; }

            public override void ExecuteResult(ControllerContext context)
            {
                var properties = new AuthenticationProperties { RedirectUri = RedirectUri };
                if (UserId != null)
                {
                    properties.Dictionary[XsrfKey] = UserId;
                }
                context.HttpContext.GetOwinContext().Authentication.Challenge(properties, LoginProvider);
            }
        }
        #endregion
    }
}