using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Xml.Linq;

namespace App.Controllers
{
    [ApiController]
    [Route("app")]
    public class AppController : ControllerBase
    {
        private readonly CoreService _core;

        public AppController(CoreService core)
        {
            _core = core;
        }

        [HttpPost("getLanguageStrings")]
        public void GetLanguageStrings() => Response.CreateBytes(ReadFile($"{_core.Resources.ResourcePath}/data/languages/en.txt"));

        [HttpPost("serverList")]
        public void ServerList()
        {
            var servers = _core.GetServerList(HttpContext.Connection.RemoteIpAddress).Select(_ => _.ToXml());
            if (servers.Any())
            {
                var response = new XElement("Servers", new XElement("Servers", servers));
                Response.CreateXml(response.ToString());
                return;
            }
            Response.CreateError("No Servers");
        }

        // The website's status block: players now, the 24-hour peak and uptime per day (StatusHistory).
        [HttpGet("status")]
        [HttpPost("status")]
        public IActionResult Status()
        {
            Response.Headers["Cache-Control"] = "no-store";
            return new JsonResult(_core.Status.Report());
        }

        [HttpPost("init")]
        public void Init() => Response.CreateBytes(ReadFile($"{_core.Resources.ResourcePath}/data/init.xml"));

        [HttpPost("globalNews")]
        public void GlobalNews() => Response.CreateBytes(ReadFile($"{_core.Resources.ResourcePath}/data/globalNews.json"));

        private static byte[] ReadFile(string path) => System.IO.File.ReadAllBytes(path);
    }
}