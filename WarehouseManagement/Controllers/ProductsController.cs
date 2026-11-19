using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace WarehouseManagement.Controllers;
[ApiController, Route("api/[controller]"), Authorize(Roles = "Admin,Manager,Clerk")]
public sealed class ProductsController(IRepository<Product> repository) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> All(CancellationToken ct) => Ok(await repository.GetAllAsync(ct));
}
