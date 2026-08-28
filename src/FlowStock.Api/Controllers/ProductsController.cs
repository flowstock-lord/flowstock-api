using FlowStock.Api.Authorization;
using FlowStock.Application.Catalog;
using FlowStock.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowStock.Api.Controllers;

/// <summary>
/// The product catalogue. Readable by every authenticated user, writable by Admin only —
/// products are master data, not a warehouse operation (docs/PLAN.md, section 25).
/// Products are never deleted, only deactivated: inventory history refers to them forever.
/// </summary>
[ApiController]
[Route("api/products")]
[Authorize(Policy = Policies.AnyAuthenticated)]
public class ProductsController(IProductService productService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ProductResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List(
        [FromQuery] ProductQuery query,
        CancellationToken cancellationToken)
        => Ok(await productService.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await productService.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Create(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        var product = await productService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Update(
        Guid id,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
        => Ok(await productService.UpdateAsync(id, request, cancellationToken));

    [HttpPost("{id:guid}/deactivate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Deactivate(Guid id, CancellationToken cancellationToken)
        => Ok(await productService.SetActiveAsync(id, isActive: false, cancellationToken));

    [HttpPost("{id:guid}/activate")]
    [Authorize(Policy = Policies.Admin)]
    [ProducesResponseType<ProductResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> Activate(Guid id, CancellationToken cancellationToken)
        => Ok(await productService.SetActiveAsync(id, isActive: true, cancellationToken));
}
