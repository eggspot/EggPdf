using EggPdf.DocGen;

// Regenerates the Fluent API reference block in site/fluent-api.html from the code + XML docs.
//   dotnet run --project tools/EggPdf.DocGen             rewrite the page
//   dotnet run --project tools/EggPdf.DocGen -- --check  exit 1 if the page is out of date (CI)
var check = args.Contains("--check");

var page = FindPage();
if (page == null)
{
    Console.Error.WriteLine("site/fluent-api.html not found above " + AppContext.BaseDirectory);
    return 2;
}

var generated = ApiReferenceGenerator.Generate(typeof(EggPdf.Fluent.Document).Assembly, "EggPdf.Fluent");
var current = File.ReadAllText(page);
var updated = PageUpdater.Replace(current, generated);

if (updated == current)
{
    Console.WriteLine("API reference is up to date.");
    return 0;
}

if (check)
{
    Console.Error.WriteLine("site/fluent-api.html API reference is out of date. Run: dotnet run --project tools/EggPdf.DocGen");
    return 1;
}

File.WriteAllText(page, updated);
Console.WriteLine("Updated " + page);
return 0;

static string? FindPage()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "site", "fluent-api.html");
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return null;
}
