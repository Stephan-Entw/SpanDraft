using System.Globalization;

namespace SpanDraft.Tests;

internal sealed class UiCultureScope : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
    private readonly CultureInfo _previousNumeric = CultureInfo.CurrentCulture;
    public UiCultureScope(string culture) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    public void Dispose() { CultureInfo.CurrentUICulture = _previous; CultureInfo.CurrentCulture = _previousNumeric; }
}
