using System;

namespace Test.Dtos
{
    /// <summary>
    /// Response DTO representing a game.
    /// </summary>
    public class GameResponse
    {
        /// <summary>
        /// Unique identifier (GUID or unique id) represented as string.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Game name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// "indoor" or "outdoor".
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Short description of the game.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Recommended age or null if not applicable.
        /// </summary>
        public int? RecommendedAge { get; set; }

        /// <summary>
        /// Indoor-specific properties when type == "indoor"; otherwise null.
        /// </summary>
        public IndoorSpecificProps IndoorSpecificProps { get; set; }

        /// <summary>
        /// Outdoor-specific properties when type == "outdoor"; otherwise null.
        /// </summary>
        public OutdoorSpecificProps OutdoorSpecificProps { get; set; }
    }
}
