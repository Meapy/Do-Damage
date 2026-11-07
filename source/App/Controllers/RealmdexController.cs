using Microsoft.AspNetCore.Mvc;

namespace App.Controllers
{
    // For RealmDex's server list: the number of players online, as plain text ("12"). On the site it
    // is https://dodmg.com/realmdex/stats (Caddy rewrites it to /api/realmdex/stats, which the
    // gateway forwards here); the account server answers it on its own port too.
    [ApiController]
    [Route("realmdex")]
    public class RealmdexController : ControllerBase
    {
        private readonly CoreService _core;

        public RealmdexController(CoreService core)
        {
            _core = core;
        }

        [HttpGet("stats")]
        [HttpPost("stats")]
        public IActionResult Stats()
        {
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            return Content(_core.Status.Current().Players.ToString(), "text/plain; charset=utf-8");
        }
    }
}
