using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace OrderProcessing.Models
{
    /// <summary>
    /// DTO representing a game returned by the Games APIs.
    /// Fields:
    /// - id: string (GUID or unique id)
    /// - name: string
    /// - type: string ("indoor" | "outdoor")
    /// - description: string
    /// - recommendedAge: int | null
    /// - indoorSpecificProps: object | null
    /// - outdoorSpecificProps: object | null
    /// </summary>
    public class GameResponse
    {
        /// <summary>
        /// Unique identifier for the game
        /// </summary>
        [Required]
        public string Id { get; set; }

        /// <summary>
        /// Name of the game
        /// </summary>
        [Required]
        public string Name { get; set; }

        /// <summary>
        /// Type of the game ("indoor" or "outdoor")
        /// </summary>
        [Required]
        public string Type { get; set; }

        /// <summary>
        /// Description of the game
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Recommended minimum age for the game
        /// </summary>
        public int? RecommendedAge { get; set; }

        /// <summary>
        /// Indoor specific properties (when type == "indoor"). Can be null when not applicable.
        /// </summary>
        public object IndoorSpecificProps { get; set; }

        /// <summary>
        /// Outdoor specific properties (when type == "outdoor"). Can be null when not applicable.
        /// </summary>
        public object OutdoorSpecificProps { get; set; }
    }
}
