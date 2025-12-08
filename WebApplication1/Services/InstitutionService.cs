using AMS.Models;
using AMS.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMS.Services
{
    public interface IInstitutionService
    {
        Task<string> GetInstitutionNameAsync();
        Task<string?> GetInstitutionLogoAsync();
        Task<string?> GetInstitutionAddressAsync();
        Task<string?> GetInstitutionPhoneAsync();
        Task<string?> GetInstitutionEmailAsync();
        Task<string?> GetAcademicYearAsync();
        Task<InstitutionInfo> GetInstitutionInfoAsync();
    }

    public class InstitutionInfo
    {
        public string Name { get; set; } = "AMS";
        public string? Logo { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? AcademicYear { get; set; }

        // Helper for getting initials
        public string Initials => string.IsNullOrWhiteSpace(Name) ? "A" : Name.Substring(0, 1).ToUpper();
    }

    public class InstitutionService : IInstitutionService
    {
        private readonly ApplicationDbContext _context;

        public InstitutionService(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task<string?> GetSettingAsync(string key)
        {
            var setting = await _context.SystemSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.SettingKey == key);
            return setting?.SettingValue;
        }

        public async Task<string> GetInstitutionNameAsync()
        {
            return await GetSettingAsync("InstitutionName") ?? "AMS";
        }

        public async Task<string?> GetInstitutionLogoAsync()
        {
            return await GetSettingAsync("InstitutionLogo");
        }

        public async Task<string?> GetInstitutionAddressAsync()
        {
            return await GetSettingAsync("InstitutionAddress");
        }

        public async Task<string?> GetInstitutionPhoneAsync()
        {
            return await GetSettingAsync("InstitutionPhone");
        }

        public async Task<string?> GetInstitutionEmailAsync()
        {
            return await GetSettingAsync("InstitutionEmail");
        }

        public async Task<string?> GetAcademicYearAsync()
        {
            return await GetSettingAsync("CurrentAcademicYear");
        }

        public async Task<InstitutionInfo> GetInstitutionInfoAsync()
        {
            var settings = await _context.SystemSettings
                .AsNoTracking()
                .Where(s => s.Category == "Branding" || s.Category == "Academic")
                .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);

            return new InstitutionInfo
            {
                Name = settings.GetValueOrDefault("InstitutionName") ?? "AMS",
                Logo = settings.GetValueOrDefault("InstitutionLogo"),
                Address = settings.GetValueOrDefault("InstitutionAddress"),
                Phone = settings.GetValueOrDefault("InstitutionPhone"),
                Email = settings.GetValueOrDefault("InstitutionEmail"),
                AcademicYear = settings.GetValueOrDefault("CurrentAcademicYear")
            };
        }
    }
}
