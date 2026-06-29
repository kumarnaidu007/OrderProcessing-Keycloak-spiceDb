using System.Collections.Generic;

namespace OrderProcessing.Models
{
    /// <summary>
    /// Generic paged result wrapper used by the API to return paginated lists.
    /// </summary>
    /// <typeparam name="T">Type of item in the paged list</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// List of items on the current page.
        /// </summary>
        public List<T> Items { get; set; }

        /// <summary>
        /// Current page number (1-based).
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Size of pages requested.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Total number of items matching the query.
        /// </summary>
        public int TotalCount { get; set; }
    }
}
