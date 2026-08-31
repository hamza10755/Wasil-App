using System.Threading.Tasks;
using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;

namespace Wasil.Service.Interfaces;

public interface ICatalogService
{
    PagedResultDto<StoreDto> ListStores(string? name, int page, int pageSize);
    Task<StoreDetailDto> GetStoreDetailsAsync(int storeId);
    Task<PagedResultDto<ProductDto>> SearchProductsAsync(
        int? storeId,
        int? categoryId,
        string? text,
        decimal? minPrice,
        decimal? maxPrice,
        bool? inStockOnly,
        string? sortBy,
        string? sortOrder,
        int page,
        int pageSize);
}
