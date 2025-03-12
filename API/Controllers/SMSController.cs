using Engine.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SMSController : ControllerBase
    {
        private readonly ILogger<SMSController> _logger;
        private readonly IRateLimiterService _service;
        public SMSController(ILogger<SMSController> logger, RateLimiterService service)
        {
            _logger = logger;
            _service = service;
        }

        [HttpPost("can-send")]
        public async Task<IActionResult> CanSendMessage([FromQuery] string phoneNumber)
        {
            if(string.IsNullOrEmpty(phoneNumber))
            {
                _logger.LogWarning("Received request with empty phone number");
                return BadRequest("Phone number is required");
            }
            try
            {
                bool isCanSend = await _service.CanSendMessageAsync(phoneNumber);
                return isCanSend ?
                    Ok(new { isCanSend }) :
                    StatusCode(429, new { isCanSend, message = "Rate limit exceeded. Try again later." });
                //return Ok(new { isCanSend });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in CanSendMessage");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
