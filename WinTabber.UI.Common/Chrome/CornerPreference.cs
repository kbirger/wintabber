using System;
using System.Collections.Generic;
using System.Text;

namespace WinTabber.UI.Common.Chrome;

public enum CornerPreference
{
    /// <summary>Let the system decide when to round window corners.</summary>
    DWMWCP_DEFAULT = 0,
    /// <summary>Never round window corners.</summary>
    DWMWCP_DONOTROUND = 1,
    /// <summary>Round the corners, if appropriate.</summary>
    DWMWCP_ROUND = 2,
    /// <summary>Round the corners if appropriate, with a small radius.</summary>
    DWMWCP_ROUNDSMALL = 3,
}