using Microsoft.AspNetCore.Mvc;
using Wasil.Service.Interfaces;

namespace Wasil.Api.Controllers;

[Route("api/v1")]
[ApiController]
public class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public CatalogController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet("stores")]
    public IActionResult ListStores([FromQuery] string? name, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = _catalogService.ListStores(name, page, pageSize);
        return Ok(result);
    }

    [HttpGet("stores/{id:int}")]
    public IActionResult GetStoreDetails(int id)
    {
        try
        {
            var result = _catalogService.GetStoreDetails(id);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpGet("products")]
    public IActionResult SearchProducts(
        [FromQuery] int? storeId,
        [FromQuery] int? categoryId,
        [FromQuery] string? text,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] bool? inStockOnly,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var result = _catalogService.SearchProducts(
            storeId, categoryId, text, minPrice, maxPrice, inStockOnly, sortBy, sortOrder, page, pageSize);
        return Ok(result);
    }
}
