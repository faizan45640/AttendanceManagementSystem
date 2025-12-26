using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AMS.Models;
using AMS.Models.ViewModels;
using AMS.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace AMS.Controllers;

public sealed class AuthController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;

    public AuthController(ApplicationDbContext context, IConfiguration configuration, IEmailService emailService)
    {
        _context = context;
        _configuration = configuration;
        _emailService = emailService;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login()
    {
        return View(new LoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // NOTE: This matches your existing logic. Consider replacing with a proper password hasher.
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u =>
                u.Email == model.Email &&
                u.PasswordHash == model.Password &&
                (u.IsActive == null || u.IsActive == true));

        if (user == null)
        {
            ViewBag.Error = "Invalid Email or Password";
            return View(model);
        }

        var token = CreateJwt(user.UserId, user.Username ?? user.Email ?? "user", user.Role ?? string.Empty);

        Response.Cookies.Append("access_token", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !HttpContext.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(1),
            Path = "/"
        });

        return (user.Role ?? string.Empty) switch
        {
            "Admin" => RedirectToAction("Dashboard", "Admin"),
            "Teacher" => RedirectToAction("Dashboard", "TeacherPortal"),
            "Student" => RedirectToAction("Dashboard", "StudentPortal"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("access_token", new CookieOptions
        {
            Path = "/"
        });

        return RedirectToAction("Login");
    }

    private string CreateJwt(int userId, string username, string role)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.StartsWith("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Jwt:Key is missing. Set a strong secret in configuration.");
        }

        var jwtIssuer = _configuration["Jwt:Issuer"];
        var jwtAudience = _configuration["Jwt:Audience"];

        var claims = new List<Claim>
        {
            new("UserId", userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username)
        };
        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: string.IsNullOrWhiteSpace(jwtIssuer) ? null : jwtIssuer,
            audience: string.IsNullOrWhiteSpace(jwtAudience) ? null : jwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Forgot Password
    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == model.Email && (u.IsActive == null || u.IsActive == true));

        if (user == null)
        {
            // Don't reveal that user doesn't exist (security best practice)
            TempData["Success"] = "If an account with that email exists, a password reset link has been sent.";
            return RedirectToAction("Login");
        }

        // Generate secure token
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        user.PasswordResetToken = token;
        user.PasswordResetTokenExpiry = DateTime.UtcNow.AddHours(24);

        await _context.SaveChangesAsync();

        // Send email
        var resetLink = Url.Action("ResetPassword", "Auth", new { token }, Request.Scheme);
        var emailSent = await _emailService.SendPasswordResetEmailAsync(user.Email!, resetLink!);

        if (!emailSent)
        {
            TempData["Error"] = "Failed to send reset email. Please contact support.";
            return RedirectToAction("ForgotPassword");
        }

        TempData["Success"] = "Password reset link has been sent to your email.";
        return RedirectToAction("Login");
    }

    // Reset Password
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            TempData["Error"] = "Invalid password reset token.";
            return RedirectToAction("Login");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.PasswordResetToken == token &&
                                      u.PasswordResetTokenExpiry > DateTime.UtcNow);

        if (user == null)
        {
            TempData["Error"] = "Invalid or expired password reset token.";
            return RedirectToAction("Login");
        }

        return View(new ResetPasswordViewModel { Token = token });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.PasswordResetToken == model.Token &&
                                      u.PasswordResetTokenExpiry > DateTime.UtcNow);

        if (user == null)
        {
            TempData["Error"] = "Invalid or expired password reset token.";
            return RedirectToAction("Login");
        }

        // Update password (Note: In production, hash this password properly!)
        user.PasswordHash = model.NewPassword;
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiry = null;

        await _context.SaveChangesAsync();

        TempData["Success"] = "Your password has been reset successfully. Please login with your new password.";
        return RedirectToAction("Login");
    }
}