using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OrderProcessing.Helpers
{
    /// <summary>
    /// Reusable validation helpers and pattern constants for country code validation
    /// and query parameter constraints used by the Countries endpoints.
    /// </summary>
    public static class CountryValidation
    {
        /// <summary>
        /// Regex pattern that matches 2 or 3 ASCII letters (alpha-2 or alpha-3 codes).
        /// Matches exactly: ^[A-Za-z]{2,3}$
        /// </summary>
        public const string CountryCodePattern = "^[A-Za-z]{2,3}$";

        /// <summary>
        /// Compiled, culture-invariant regex for validating country codes.
        /// </summary>
        public static readonly Regex CountryCodeRegex = new Regex(CountryCodePattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Default page number (1-based) for list endpoints.
        /// </summary>
        public const int DefaultPage = 1;

        /// <summary>
        /// Default page size for list endpoints.
        /// </summary>
        public const int DefaultSize = 50;

        /// <summary>
        /// Maximum allowed page size for list endpoints.
        /// </summary>
        public const int MaxSize = 500;

        /// <summary>
        /// Validates and normalizes a country code.
        /// Trimming is performed and result is upper-cased using InvariantCulture.
        /// Returns true when the input is a well-formed 2- or 3-letter code.
        /// On success <paramref name="normalized"/> will contain the canonical (uppercased) code.
        /// On failure <paramref name="errorMessage"/> will contain a brief reason suitable for returning
        /// in a validation ProblemDetails detail/message.
        /// </summary>
        public static bool TryNormalizeAndValidateCode(string? input, out string? normalized, out string? errorMessage)
        {
            normalized = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                errorMessage = "Country code must be provided.";
                return false;
            }

            var trimmed = input.Trim();

            if (!CountryCodeRegex.IsMatch(trimmed))
            {
                errorMessage = "Country code must consist of 2 or 3 alphabetic characters.";
                return false;
            }

            normalized = trimmed.ToUpper(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Validates a 1-based page number. Returns true when valid; otherwise false and an error message.
        /// </summary>
        public static bool ValidatePage(int page, out string? errorMessage)
        {
            errorMessage = null;
            if (page < 1)
            {
                errorMessage = "Page must be greater than or equal to 1.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates page size (must be between 1 and MaxSize inclusive). Returns true when valid;
        /// otherwise false and an error message.
        /// </summary>
        public static bool ValidateSize(int size, out string? errorMessage)
        {
            errorMessage = null;
            if (size < 1)
            {
                errorMessage = "Size must be greater than or equal to 1.";
                return false;
            }

            if (size > MaxSize)
            {
                errorMessage = $"Size must be less than or equal to {MaxSize}.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Apply defaults for nullable page/size inputs and validate. Returns true when both are valid.
        /// On success validatedPage/validatedSize contain resolved values. On failure errorMessage contains the reason.
        /// </summary>
        public static bool TryValidatePaging(int? page, int? size, out int validatedPage, out int validatedSize, out string? errorMessage)
        {
            validatedPage = page ?? DefaultPage;
            validatedSize = size ?? DefaultSize;
            errorMessage = null;

            if (!ValidatePage(validatedPage, out var pageError))
            {
                errorMessage = pageError;
                return false;
            }

            if (!ValidateSize(validatedSize, out var sizeError))
            {
                errorMessage = sizeError;
                return false;
            }

            return true;
        }
    }
}
