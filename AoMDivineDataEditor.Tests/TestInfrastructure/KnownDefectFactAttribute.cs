using Xunit;

namespace AoMDivineDataEditor.Tests;

// Opt-in diagnostics assert the desired behavior, never the known broken behavior.
public sealed class KnownDefectFactAttribute : FactAttribute
{
    public KnownDefectFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AOM_RUN_KNOWN_FAILURES") != "1")
            Skip = "Known safety defect: set AOM_RUN_KNOWN_FAILURES=1 to run the failing acceptance check. Required release verification enables it.";
    }
}
