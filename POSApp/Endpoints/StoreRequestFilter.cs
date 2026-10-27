using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace POSApp.Endpoints;

public sealed class StoreRequestFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            if (HttpMethods.IsPost(context.HttpContext.Request.Method))
            {
                var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }

            return await next(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest(new InventoryApiError("The request token expired. Please try again."));
        }
        catch (ValidationException exception)
        {
            return Results.BadRequest(new InventoryApiError(exception.Message));
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(new InventoryApiError("Your account no longer has access to this operation."), statusCode: 403);
        }
        catch (Exception exception) when (exception is DbUpdateException or DbException)
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<StoreRequestFilter>>();
            logger.LogError(exception, "Store request could not commit.");
            return Results.Conflict(new InventoryApiError("Could not save. Retry the same request; it won't be recorded twice."));
        }
    }
}
