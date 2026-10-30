using Microsoft.AspNetCore.Mvc;
using UluBilge.Api.Authentication;
using UluBilge.Api.Models;
using UluBilge.App.Queries.ReportHandler;
using UluBilge.Core.Enums;

namespace UluBilge.Api.Controllers
{
    [RoleAuthorize(RoleEnum.Report, RoleEnum.Admin)]
    public sealed class ReportController : BaseController
    {
        [HttpGet]
        public async Task<ActionResult<BaseResponse<GetProductSalesAsync.Result>>> GetProductSalesAsync([FromQuery] GetProductSalesAsync.Query query, CancellationToken ct)
        {
            var result = await AsyncQueryHandler<GetProductSalesAsync.Query, GetProductSalesAsync.Result>().HandleAsync(query, ct);
            return Ok(new BaseResponse<GetProductSalesAsync.Result>(result));
        }
    }
}
