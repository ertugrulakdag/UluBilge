using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using UluBilge.Api.Authentication;
using UluBilge.Api.Models;
using UluBilge.App.Commands.RegionHandler;
using UluBilge.App.Queries.RegionHandler;
using UluBilge.Core.Enums;

namespace UluBilge.Api.Controllers
{
    [RoleAuthorize(RoleEnum.Admin, RoleEnum.Region)]
    public sealed class RegionController : BaseController
    {
        [HttpGet("{id:int}")]
        public async Task<ActionResult<BaseResponse<GetRegionAsync.Result>>> GetRegionAsync(int id, CancellationToken ct)
        {
            var result = await AsyncQueryHandler<GetRegionAsync.Query, GetRegionAsync.Result>().HandleAsync(new GetRegionAsync.Query { RegionId = id }, ct);
            return Ok(new BaseResponse<GetRegionAsync.Result>(result));
        }

        [HttpGet]
        public async Task<ActionResult<BaseResponse<GetRegionsAsync.Result>>> GetRegionsAsync([FromQuery] GetRegionsAsync.Query query, CancellationToken ct)
        {
            var result = await AsyncQueryHandler<GetRegionsAsync.Query, GetRegionsAsync.Result>().HandleAsync(query, ct);
            return Ok(new BaseResponse<GetRegionsAsync.Result>(result));
        }

        [HttpPost]
        public async Task<ActionResult<BaseResponse<AddRegionAsync.Result>>> AddAsync(
            [FromBody, Required(ErrorMessage = "İstek gövdesi zorunludur.")] AddRegionAsync.Command command,
            CancellationToken ct)
        {
            var result = await AsyncCommandHandler<AddRegionAsync.Command, AddRegionAsync.Result>().HandleAsync(command, ct);
            return Ok(new BaseResponse<AddRegionAsync.Result>(result));
        }

        [HttpPut]
        public async Task<ActionResult<BaseResponse<UpdateRegionAsync.Result>>> UpdateAsync(
            [FromBody, Required(ErrorMessage = "İstek gövdesi zorunludur.")] UpdateRegionAsync.Command command,
            CancellationToken ct)
        {
            var result = await AsyncCommandHandler<UpdateRegionAsync.Command, UpdateRegionAsync.Result>().HandleAsync(command, ct);
            return Ok(new BaseResponse<UpdateRegionAsync.Result>(result));
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult<BaseResponse<DeleteRegionAsync.Result>>> DeleteAsync(int id, CancellationToken ct)
        {
            var result = await AsyncCommandHandler<DeleteRegionAsync.Command, DeleteRegionAsync.Result>().HandleAsync(new DeleteRegionAsync.Command { RegionId = id }, ct);
            return Ok(new BaseResponse<DeleteRegionAsync.Result>(result));
        }
    }
}
