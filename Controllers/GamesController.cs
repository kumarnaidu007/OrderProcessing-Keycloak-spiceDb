using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Test.Dtos;

namespace Test.Controllers
{
    [ApiController]
    [Route("api/games")]
    public class GamesController : ControllerBase
    {
        // Hardcoded representative data list. Keep minimal and representative.
        private static readonly List<GameResponse> _games = new List<GameResponse>
        {
            new GameResponse
            {
                Id = "c1a2b3d4-e5f6-47ab-8cde-000000000001",
                Name = "Chess",
                Type = "indoor",
                Description = "Classic strategy board game.",
                RecommendedAge = 6,
                IndoorSpecificProps = new IndoorSpecificProps { MinPlayers = 2 },
                OutdoorSpecificProps = null
            },
            new GameResponse
            {
                Id = "c1a2b3d4-e5f6-47ab-8cde-000000000002",
                Name = "Carrom",
                Type = "indoor",
                Description = "Board game popular in South Asia.",
                RecommendedAge = 8,
                IndoorSpecificProps = new IndoorSpecificProps { MinPlayers = 2 },
                OutdoorSpecificProps = null
            },
            new GameResponse
            {
                Id = "d4c3b2a1-e5f6-47ab-8cde-000000000010",
                Name = "Frisbee",
                Type = "outdoor",
                Description = "Flying disc game.",
                RecommendedAge = 8,
                IndoorSpecificProps = null,
                OutdoorSpecificProps = new OutdoorSpecificProps { MinPlayers = 2 }
            },
            new GameResponse
            {
                Id = "d4c3b2a1-e5f6-47ab-8cde-000000000011",
                Name = "Football",
                Type = "outdoor",
                Description = "Team sport played outdoors.",
                RecommendedAge = 10,
                IndoorSpecificProps = null,
                OutdoorSpecificProps = new OutdoorSpecificProps { MinPlayers = 2 }
            }
        };

        /// <summary>
        /// Returns a paginated list of indoor games.
        /// </summary>
        /// <remarks>
        /// Example response:
        /// [
        ///   {
        ///     "id": "c1a2b3d4-e5f6-47ab-8cde-000000000001",
        ///     "name": "Chess",
        ///     "type": "indoor",
        ///     "description": "Classic strategy board game.",
        ///     "recommendedAge": 6,
        ///     "indoorSpecificProps": { "minPlayers": 2 },
        ///     "outdoorSpecificProps": null
        ///   }
        /// ]
        /// </remarks>
        /// <param name="name">Optional name search (substring, case-insensitive, max length 200)</param>
        /// <param name="pageSize">Page size (default 50)</param>
        /// <param name="pageNumber">Page number (default 1)</param>
        /// <returns>Paginated list of indoor games</returns>
        [HttpGet("indoor")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(IEnumerable<GameResponse>), 200)]
        [ProducesResponseType(400)]
        public IActionResult GetIndoor([FromQuery] string name = null, [FromQuery] int pageSize = 50, [FromQuery] int pageNumber = 1)
        {
            if (pageSize <= 0 || pageNumber <= 0)
            {
                return BadRequest("pageSize and pageNumber must be positive integers.");
            }

            if (!string.IsNullOrEmpty(name) && name.Length > 200)
            {
                return BadRequest("name query parameter must be at most 200 characters.");
            }

            var query = _games.Where(g => string.Equals(g.Type, "indoor", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(name))
            {
                query = query.Where(g => g.Name != null && g.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var total = query.Count();
            var items = query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var result = new
            {
                items,
                pageNumber,
                pageSize,
                totalCount = total,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            };

            return Ok(result);
        }

        /// <summary>
        /// Returns a paginated list of outdoor games.
        /// </summary>
        /// <remarks>
        /// Example response:
        /// [
        ///   {
        ///     "id": "d4c3b2a1-e5f6-47ab-8cde-000000000010",
        ///     "name": "Frisbee",
        ///     "type": "outdoor",
        ///     "description": "Flying disc game.",
        ///     "recommendedAge": 8,
        ///     "indoorSpecificProps": null,
        ///     "outdoorSpecificProps": { "minPlayers": 2 }
        ///   }
        /// ]
        /// </remarks>
        /// <param name="name">Optional name search (substring, case-insensitive, max length 200)</param>
        /// <param name="pageSize">Page size (default 50)</param>
        /// <param name="pageNumber">Page number (default 1)</param>
        /// <returns>Paginated list of outdoor games</returns>
        [HttpGet("outdoor")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(IEnumerable<GameResponse>), 200)]
        [ProducesResponseType(400)]
        public IActionResult GetOutdoor([FromQuery] string name = null, [FromQuery] int pageSize = 50, [FromQuery] int pageNumber = 1)
        {
            if (pageSize <= 0 || pageNumber <= 0)
            {
                return BadRequest("pageSize and pageNumber must be positive integers.");
            }

            if (!string.IsNullOrEmpty(name) && name.Length > 200)
            {
                return BadRequest("name query parameter must be at most 200 characters.");
            }

            var query = _games.Where(g => string.Equals(g.Type, "outdoor", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(name))
            {
                query = query.Where(g => g.Name != null && g.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var total = query.Count();
            var items = query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var result = new
            {
                items,
                pageNumber,
                pageSize,
                totalCount = total,
                totalPages = (int)Math.Ceiling(total / (double)pageSize)
            };

            return Ok(result);
        }
    }
}
