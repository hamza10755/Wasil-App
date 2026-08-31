using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Wasil.Data.Entities;
using Wasil.Service.DTOs;
using Wasil.Service.DTOs.Shared;
using Wasil.Service.Interfaces;

namespace Wasil.Service.Services;

public class CatalogService : ICatalogService
{
    private readonly WasilDbContext _dbContext;
    private readonly CacheService _cacheService;

    public CatalogService(WasilDbContext dbContext, CacheService cacheService)
    {
        _dbContext = dbContext;
        _cacheService = cacheService;
    }

    public PagedResultDto<StoreDto> ListStores(string? name, int page, int pageSize)
    {
        if (page < 1)
            page = 1;
        if (pageSize < 1)
            pageSize = 10;

        var query = _dbContext.Stores.AsQueryable().IgnoreQueryFilters();

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(s => s.StoreName != null && s.StoreName.Contains(name));
        }

        var totalCount = query.Count();

        var items = query
            .OrderBy(s => s.StoreName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new StoreDto
            {
                Id = s.Id,
                StoreName = s.StoreName ?? string.Empty,
                StoreLocation = s.StoreLocation ?? string.Empty,
                Status = s.Status
            })
            .ToList();

        return new PagedResultDto<StoreDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<StoreDetailDto> GetStoreDetailsAsync(int storeId)
    {
        var key = $"store:{storeId}:details";
        var cached = await _cacheService.GetAsync<StoreDetailDto>(key, "store-details");
        if (cached != null)
        {
            return cached;
        }

        var storeDetails = await _dbContext.Stores
            .Where(s => s.Id == storeId)
            .Select(s => new StoreDetailDto
            {
                Id = s.Id,
                StoreName = s.StoreName ?? string.Empty,
                StoreLocation = s.StoreLocation ?? string.Empty,
                Status = s.Status,
                ProductCount = s.Products.Count(p => !p.IsDeleted)
            })
            .FirstOrDefaultAsync();

        if (storeDetails == null)
            throw new KeyNotFoundException($"Store with ID {storeId} not found.");

        await _cacheService.SetAsync(key, "store-details", storeDetails, TimeSpan.FromMinutes(5));

        return storeDetails;
    }

    public async Task<PagedResultDto<ProductDto>> SearchProductsAsync(
        int? storeId,
        int? categoryId,
        string? text,
        decimal? minPrice,
        decimal? maxPrice,
        bool? inStockOnly,
        string? sortBy,
        string? sortOrder,
        int page,
        int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize != 10 && pageSize != 20 && pageSize != 50) pageSize = 20;

        // Skip cache if searching by text or filtering by price
        bool bypassCache = !string.IsNullOrWhiteSpace(text) || minPrice.HasValue || maxPrice.HasValue;

        if (!bypassCache)
        {
            var key = $"store:{storeId ?? 0}:products:cat:{categoryId?.ToString() ?? "all"}:sort:{sortBy ?? "name"}:{sortOrder ?? "asc"}:page:{page}:size:{pageSize}";
            var cached = await _cacheService.GetAsync<PagedResultDto<ProductDto>>(key, "products-browse");
            if (cached != null)
            {
                return cached;
            }
        }

        var query = _dbContext.Products.AsQueryable();

        if (storeId.HasValue)
        {
            query = query.Where(p => p.StoreId == storeId.Value);
        }

        if (categoryId.HasValue)
        {
            query = query.Where(p => p.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            query = query.Where(p => p.Name != null && p.Name.Contains(text));
        }

        if (minPrice.HasValue)
        {
            query = query.Where(p => p.Price >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(p => p.Price <= maxPrice.Value);
        }

        if (inStockOnly.HasValue && inStockOnly.Value)
        {
            query = query.Where(p => p.StockQuantity > 0);
        }

        bool isDescending = string.Equals(sortOrder, "desc", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(sortBy, "price", StringComparison.OrdinalIgnoreCase))
        {
            query = isDescending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price);
        }
        else
        {
            query = isDescending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                StoreId = p.StoreId,
                CategoryId = p.CategoryId,
                Name = p.Name ?? string.Empty,
                Price = p.Price,
                StockQuantity = p.StockQuantity,
                Availability = p.Availability
            })
            .ToListAsync();

        var result = new PagedResultDto<ProductDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        if (!bypassCache)
        {
            var key = $"store:{storeId ?? 0}:products:cat:{categoryId?.ToString() ?? "all"}:sort:{sortBy ?? "name"}:{sortOrder ?? "asc"}:page:{page}:size:{pageSize}";
            await _cacheService.SetAsync(key, "products-browse", result, TimeSpan.FromSeconds(60));
        }

        return result;
    }
}
