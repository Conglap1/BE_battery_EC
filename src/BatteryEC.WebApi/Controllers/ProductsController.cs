using BatteryEC.Application.Common.Models;
using BatteryEC.Application.DTOs.Catalog;
using BatteryEC.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BatteryEC.WebApi.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ProductDto>>>> GetProducts(
        [FromQuery] int? categoryId,
        [FromQuery] string? search,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var products = await _productService.GetProductsAsync(categoryId, search, page, pageSize, cancellationToken);
        return Ok(ApiResponse<List<ProductDto>>.Ok(products));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<ProductDto>>> GetProductById(long id, CancellationToken cancellationToken)
    {
        var product = await _productService.GetProductByIdAsync(id, cancellationToken);
        if (product == null)
        {
            return NotFound(ApiResponse<ProductDto>.Fail($"Product #{id} not found."));
        }

        return Ok(ApiResponse<ProductDto>.Ok(product));
    }
}
