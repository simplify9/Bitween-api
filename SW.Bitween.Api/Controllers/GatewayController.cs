using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace SW.Bitween.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GatewayController: ControllerBase
{

    [HttpPost("{gatewayApiName}")]
    public async Task<IActionResult> Post([FromRoute] string gatewayApiName)
    {
        
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        
        return Ok();
    }
}