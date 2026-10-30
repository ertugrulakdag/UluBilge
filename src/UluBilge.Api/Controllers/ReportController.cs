using UluBilge.Api.Authentication;
using UluBilge.Core.Enums;

namespace UluBilge.Api.Controllers
{
    [RoleAuthorize(RoleEnum.Report, RoleEnum.Admin)]
    public sealed class ReportController : BaseController
    {

    }
}
