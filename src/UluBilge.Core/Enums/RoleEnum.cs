using System.ComponentModel;

namespace UluBilge.Core.Enums
{
    [Flags]
    public enum RoleEnum : long
    {
        [Description("Yok")]
        None = 0,

        [Description("Admin")]
        Admin = 1,

        [Description("Raporlama")]
        Report = 2,

        [Description("Region")]
        Region = 4,
    }
}
