using System.Collections.Generic;

namespace AMS.Models.ViewModels
{
    public sealed class BatchFilterViewModel : PagedViewModel
    {
        public List<BatchListItemViewModel> Batches { get; set; } = new();
    }

    public sealed class BatchListItemViewModel
    {
        public int BatchId { get; set; }
        public string? BatchName { get; set; }
        public int? Year { get; set; }
        public bool IsActive { get; set; }
        public int StudentCount { get; set; }
    }
}
