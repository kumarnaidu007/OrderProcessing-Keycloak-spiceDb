using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using OrderProcessing.Models;

namespace OrderProcessing.Controllers
{
    /// <summary>
    /// GamesController - exposes indoor and outdoor games. Data is hardcoded and served as a paged response.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class GamesController : ControllerBase
    {
        // Minimal representative hardcoded data. No DB or external config.
        private static readonly List<GameResponse> Games = new List<GameResponse>
        {
            new GameResponse
            {
                Id = "c1a2b3d4-0000-0000-0000-000000000001",
                Name = "Chess",
                Type = "indoor",
                Description = "Classic strategy board game.",
                RecommendedAge = 6,
                IndoorSpecificProps = new Dictionary<string, object> { { "minPlayers", 2 } },
                OutdoorSpecificProps = null
            },
            new GameResponse
            {
                Id = "c1a2b3d4-0000-0000-0000-000000000002",
                Name = "Table Tennis",
                Type = "indoor",
                Description = "Fast-paced racket sport played on a table.",
                RecommendedAge = 8,
                IndoorSpecificProps = new Dictionary<string, object> { { "minPlayers", 2 }, { "equipment", "paddles" } },
                OutdoorSpecificProps = null
            },
            new GameResponse
            {
                Id = "d4c3b2a1-0000-0000-0000-000000000001",
                Name = "Frisbee",
                Type = "outdoor",
                Description = "Flying disc game.",
                RecommendedAge = 8,
                IndoorSpecificProps = null,
                OutdoorSpecificProps = new Dictionary<string, object> { { "minPlayers", 2 } }
            },
            new GameResponse
            {
                Id = "d4c3b2a1-0000-0000-0000-000000000002",
                Name = "Soccer",
                Type = "outdoor",
                Description = "Team sport played with a spherical ball.",
                RecommendedAge = 7,
                IndoorSpecificProps = null,
                OutdoorSpecificProps = new Dictionary<string, object> { { "minPlayers", 10 } }
            }
        };

        /// <summary>
        /// Returns a paginated list of indoor games.
        /// </summary>
        /// <param name="name">Optional name search (substring, case-insensitive, max length 200)</param>
        /// <param name="pageSize">Page size. Default 50.</param>
        /// <param name="pageNumber">Page number (1-based). Default 1.</param>
        /// <returns>Paged list of indoor games.</returns>
        [HttpGet("indoor")] 
        [Produces("application/json")]
        [ProducesResponseType(typeof(PagedResult<GameResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult GetIndoor(
            [FromQuery][StringLength(200, ErrorMessage = "name cannot be longer than 200 characters")] string name = null,
            [FromQuery][Range(1, int.MaxValue)] int pageSize = 50,
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1)
        {
            if (name != null && name.Length > 200)
            {
                return BadRequest("Query parameter 'name' cannot be longer than 200 characters.");
            }

            var query = Games.Where(g => string.Equals(g.Type, "indoor", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(name))
            {
                query = query.Where(g => g.Name != null && g.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var total = query.Count();

            var items = query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var result = new PagedResult<GameResponse>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = total
            };

            return Ok(result);
        }

        /// <summary>
        /// Returns a paginated list of outdoor games.
        /// </summary>
        /// <param name="name">Optional name search (substring, case-insensitive, max length 200)</param>
        /// <param name="pageSize">Page size. Default 50.</param>
        /// <param name="pageNumber">Page number (1-based). Default 1.</param>
        /// <returns>Paged list of outdoor games.</returns>
        [HttpGet("outdoor")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(PagedResult<GameResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult GetOutdoor(
            [FromQuery][StringLength(200, ErrorMessage = "name cannot be longer than 200 characters")] string name = null,
            [FromQuery][Range(1, int.MaxValue)] int pageSize = 50,
            [FromQuery][Range(1, int.MaxValue)] int pageNumber = 1)
        {
            if (name != null && name.Length > 200)
            {
                return BadRequest("Query parameter 'name' cannot be longer than 200 characters.");
            }

            var query = Games.Where(g => string.Equals(g.Type, "outdoor", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(name))
            {
                query = query.Where(g => g.Name != null && g.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var total = query.Count();

            var items = query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var result = new PagedResult<GameResponse>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = total
            };

            return Ok(result);
        }
    }
}
