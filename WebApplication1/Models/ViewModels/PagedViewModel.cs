using System;
using System.Collections.Generic;

namespace AMS.Models.ViewModels
{
    public abstract class PagedViewModel
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        public int TotalCount { get; set; }

        public int TotalPages
        {
            get
            {
                if (PageSize <= 0) return 0;
                return (int)Math.Ceiling(TotalCount / (double)PageSize);
            }
        }

        public IReadOnlyList<int> PageSizeOptions { get; } = new[] { 10, 20, 50, 100 };
    }
}
