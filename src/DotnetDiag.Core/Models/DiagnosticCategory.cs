namespace DotnetDiag.Core.Models;

public enum DiagnosticCategory
{
    Other,
    CSharpCompiler,
    FSharpCompiler,
    VisualBasicCompiler,
    MSBuild,
    NuGet,
    DotnetSdk,
    Analyzer,
}

public static class DiagnosticCategories
{
    public static DiagnosticCategory FromCode(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return DiagnosticCategory.Other;
        }

        var prefix = new string(code.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        return prefix switch
        {
            "CS" => DiagnosticCategory.CSharpCompiler,
            "FS" => DiagnosticCategory.FSharpCompiler,
            "BC" => DiagnosticCategory.VisualBasicCompiler,
            "MSB" => DiagnosticCategory.MSBuild,
            "NU" => DiagnosticCategory.NuGet,
            "NETSDK" => DiagnosticCategory.DotnetSdk,
            "CA" or "IDE" or "SA" or "RS" or "AD" or "ASP" or "XUNIT" or "NUNIT" or "MSTEST" => DiagnosticCategory.Analyzer,
            _ => DiagnosticCategory.Other,
        };
    }

    public static string DisplayName(this DiagnosticCategory category) => category switch
    {
        DiagnosticCategory.CSharpCompiler => "C# compiler",
        DiagnosticCategory.FSharpCompiler => "F# compiler",
        DiagnosticCategory.VisualBasicCompiler => "VB compiler",
        DiagnosticCategory.MSBuild => "MSBuild",
        DiagnosticCategory.NuGet => "NuGet",
        DiagnosticCategory.DotnetSdk => ".NET SDK",
        DiagnosticCategory.Analyzer => "Analyzer",
        _ => "Other",
    };
}
