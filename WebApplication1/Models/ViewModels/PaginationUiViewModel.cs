using System.Collections.Generic;

namespace AMS.Models.ViewModels
{
    public sealed class PaginationUiViewModel
    {
        public string Action { get; set; } = string.Empty;
        public string? Controller { get; set; }

        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }

        public int TotalPages
        {
            get
            {
                if (PageSize <= 0) return 0;
                return (int)System.Math.Ceiling(TotalCount / (double)PageSize);
            }
        }

        public IReadOnlyList<int> PageSizeOptions { get; set; } = new[] { 10, 20, 50, 100 };

        public Dictionary<string, string?> RouteValues { get; set; } = new();
    }
}
