using AMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace AMS.ViewComponents
{
    public class InstitutionInfoViewComponent : ViewComponent
    {
        private readonly IInstitutionService _institutionService;

        public InstitutionInfoViewComponent(IInstitutionService institutionService)
        {
            _institutionService = institutionService;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var info = await _institutionService.GetInstitutionInfoAsync();
            return View(info);
        }
    }
}
