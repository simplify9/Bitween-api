using System;
using System.Collections.Generic;
using System.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween;

public static class Semver
{
    public static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        var parts = version.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return (0, 0, 0);
        }

        return (major, minor, patch);
    }

    public static bool IsVersionNumber(string mode)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(mode, @"^\d+\.\d+\.\d+(-\S+)?$");
    }
}