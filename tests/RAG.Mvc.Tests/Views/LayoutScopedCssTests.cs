using Xunit;
using Xunit.Sdk;

namespace RAG.Mvc.Tests.Views;

/// <summary>
/// UDS-8: the scoped styles in <c>rag/Views/Shared/_Layout.cshtml.css</c> must
/// derive brand colors from the design-system tokens (<c>var(--bs-*)</c>)
/// instead of hardcoded hex values. The stylesheet is a static source asset in
/// the repo, so the tests locate it by walking up from the test output
/// directory (<see cref="AppContext.BaseDirectory"/>) to the repo root.
/// </summary>
public class LayoutScopedCssTests
{
    private static readonly string[] HardcodedBrandHexes = ["#0077cc", "#1b6ec2", "#1861ac"];

    private static string LocateLayoutScopedCss()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "rag", "Views", "Shared", "_Layout.cshtml.css");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new XunitException(
            "Could not locate rag/Views/Shared/_Layout.cshtml.css walking up from test output.");
    }

    // ── UDS-8: no hardcoded brand hexes remain ──

    [Fact]
    public void LayoutScopedCss_HasNoHardcodedBrandHexes()
    {
        var css = File.ReadAllText(LocateLayoutScopedCss());

        foreach (var hex in HardcodedBrandHexes)
        {
            Assert.DoesNotContain(hex, css, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── UDS-8: link, primary-button and active-nav-pill resolve from tokens ──

    [Fact]
    public void LayoutScopedCss_ResolvesBrandColorsFromTokens()
    {
        var css = File.ReadAllText(LocateLayoutScopedCss());

        // Link color resolves from the shared token.
        Assert.Contains("var(--bs-link-color)", css);
        // Primary button and active nav pill resolve from the primary token.
        Assert.Contains("var(--bs-primary)", css);
        // Text on primary surfaces stays white (unchanged).
        Assert.Contains("color: #fff", css);
    }
}
