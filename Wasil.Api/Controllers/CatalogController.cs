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
    public async System.Threading.Tasks.Task<IActionResult> GetStoreDetails(int id)
    {
        var result = await _catalogService.GetStoreDetailsAsync(id);
        return Ok(result);
    }

    [HttpGet("products")]
    public async System.Threading.Tasks.Task<IActionResult> SearchProducts(
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
        if (pageSize != 10 && pageSize != 20 && pageSize != 50)
        {
            pageSize = 20;
        }

        var result = await _catalogService.SearchProductsAsync(
            storeId, categoryId, text, minPrice, maxPrice, inStockOnly, sortBy, sortOrder, page, pageSize);
        return Ok(result);
    }
}
