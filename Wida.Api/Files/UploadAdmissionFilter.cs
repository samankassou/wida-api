using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Wida.Api.Files;

public sealed class UploadAdmissionFilter : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var admin = context.HttpContext.User.IsInRole("Admin");
        var request = context.HttpContext.Request;
        var limit = admin ? (long?)null : DocumentFilePolicy.MaximumRequestBytes;
        if (limit is { } maximum && request.ContentLength > maximum)
        {
            context.Result = new ObjectResult(new { detail = "Choose a file no larger than 4 MiB." }) { StatusCode = 413 };
            return;
        }
        var size = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (size is { IsReadOnly: false }) size.MaxRequestBodySize = limit;
        context.HttpContext.Features.Set<IFormFeature>(new FormFeature(request,
            new FormOptions { MultipartBodyLengthLimit = limit ?? long.MaxValue }));
        await next();
    }
}
