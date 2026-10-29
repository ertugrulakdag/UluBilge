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

        [Description("xxx")]
        Xxx = 2,

        [Description("yyy")]
        Yyy = 4,
    }
}
