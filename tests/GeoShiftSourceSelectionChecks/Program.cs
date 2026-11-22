using Archer.MissionControl.Services;
using File = Archer.MissionControl.Services.GitHubRepositoryReader.ReviewableFile;

var files = new File[]
{
    new("GeoShift.Core/KmlCsvConverter.cs", "sha", 3000),
    new("GeoShift.Core/KmzAnalyzer.cs", "sha", 1000),
    new("GeoShift.Tests/KmlCsvTests.cs", "sha", 1000),
    new("GeoShift.Web/Program.cs", "sha", 1000),
    new("GeoShift.Core/KmlCsvHuge.cs", "sha", 9001),
    new("Other/KmlCsvConverter.cs", "sha", 1000)
};
var selected = GeoShiftSourceSelection.Select(files, "Fix KML CSV converter quoting for geographic coordinates.");
if (!selected.SequenceEqual(new[] { "GeoShift.Core/KmlCsvConverter.cs" }))
    throw new Exception("Incorrect selection or unsafe scope.");
void Reject(IEnumerable<File> candidates, string task)
{
    try { GeoShiftSourceSelection.Select(candidates, task); }
    catch (ArgumentException) { return; }
    throw new Exception("Expected selection to fail closed.");
}
Reject(files, "Make the entire application better.");
Reject(files.Concat(new[]
{
    new File("GeoShift.Core/KmlCsvOther.cs", "sha", 1000),
    new File("GeoShift.Core/KmlCsvFormat.cs", "sha", 1000)
}), "Fix KML CSV data.");
Console.WriteLine("PASS: deterministic GeoShift selection, scope and ambiguity gates.");
