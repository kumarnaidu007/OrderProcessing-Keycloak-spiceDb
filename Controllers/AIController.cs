using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace OrderProcessing.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AIController : ControllerBase
    {
        private readonly ILogger<AIController> _logger;

        public AIController(ILogger<AIController> logger)
        {
            _logger = logger;
        }

        // GET: api/ai
        // This endpoint requires authentication
        [HttpGet]
        [Authorize]
        public ActionResult<string> Get()
        {
            _logger.LogInformation("Authenticated request received for AI welcome endpoint.");
            return Ok("Welcome AI");
        }
    }
}
