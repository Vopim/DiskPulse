using System;
using System.Security.Principal;

namespace DiskPulse
{
    internal static class RuntimeInfo
    {
        // S-1-5-12 is the RESTRICTED SID. A normal Explorer launch does not
        // carry it, but sandboxed/restricted launch contexts do.
        public static bool IsRestrictedProcess()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    foreach (IdentityReference group in identity.Groups)
                    {
                        if (string.Equals(group.Value, "S-1-5-12", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }
    }
}
