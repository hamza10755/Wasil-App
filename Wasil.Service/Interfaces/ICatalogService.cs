using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;

namespace Wasil.Service.Interfaces;

public interface ICatalogService
{
    PagedResultDto<StoreDto> ListStores(string? name, int page, int pageSize);
    StoreDetailDto GetStoreDetails(int storeId);
    PagedResultDto<ProductDto> SearchProducts(
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
