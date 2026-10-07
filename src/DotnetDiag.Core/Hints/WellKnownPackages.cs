namespace DotnetDiag.Core.Hints;

/// <summary>Maps commonly used type names, namespaces and extension methods to the NuGet package that provides them.</summary>
public static class WellKnownPackages
{
    private static readonly (string Namespace, string Package)[] Namespaces =
    [
        ("Newtonsoft.Json", "Newtonsoft.Json"),
        ("Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions"),
        ("Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.DependencyInjection"),
        ("Microsoft.Extensions.Configuration", "Microsoft.Extensions.Configuration"),
        ("Microsoft.Extensions.Options", "Microsoft.Extensions.Options"),
        ("Microsoft.Extensions.Hosting", "Microsoft.Extensions.Hosting"),
        ("Microsoft.Extensions.Http", "Microsoft.Extensions.Http"),
        ("Microsoft.Extensions.Caching.Memory", "Microsoft.Extensions.Caching.Memory"),
        ("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore"),
        ("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient"),
        ("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite"),
        ("Microsoft.AspNetCore.Mvc.Testing", "Microsoft.AspNetCore.Mvc.Testing"),
        ("Microsoft.SemanticKernel", "Microsoft.SemanticKernel"),
        ("Microsoft.IdentityModel.Tokens", "Microsoft.IdentityModel.Tokens"),
        ("System.IdentityModel.Tokens.Jwt", "System.IdentityModel.Tokens.Jwt"),
        ("System.CommandLine", "System.CommandLine"),
        ("Dapper", "Dapper"),
        ("AutoMapper", "AutoMapper"),
        ("MediatR", "MediatR"),
        ("Polly", "Polly"),
        ("Serilog", "Serilog"),
        ("NLog", "NLog"),
        ("FluentValidation", "FluentValidation"),
        ("FluentAssertions", "FluentAssertions"),
        ("Shouldly", "Shouldly"),
        ("Moq", "Moq"),
        ("NSubstitute", "NSubstitute"),
        ("Xunit", "xunit"),
        ("NUnit.Framework", "NUnit"),
        ("Microsoft.VisualStudio.TestTools.UnitTesting", "MSTest.TestFramework"),
        ("Bogus", "Bogus"),
        ("CsvHelper", "CsvHelper"),
        ("YamlDotNet", "YamlDotNet"),
        ("RestSharp", "RestSharp"),
        ("StackExchange.Redis", "StackExchange.Redis"),
        ("MongoDB.Driver", "MongoDB.Driver"),
        ("Npgsql", "Npgsql"),
        ("Spectre.Console", "Spectre.Console"),
        ("HtmlAgilityPack", "HtmlAgilityPack"),
        ("Humanizer", "Humanizer"),
        ("BenchmarkDotNet", "BenchmarkDotNet"),
        ("Grpc.Net.Client", "Grpc.Net.Client"),
        ("Google.Protobuf", "Google.Protobuf"),
        ("Azure.Storage.Blobs", "Azure.Storage.Blobs"),
        ("Azure.Identity", "Azure.Identity"),
        ("Swashbuckle.AspNetCore", "Swashbuckle.AspNetCore"),
        ("MassTransit", "MassTransit"),
        ("Hangfire", "Hangfire.Core"),
        ("Quartz", "Quartz"),
        ("OpenTelemetry", "OpenTelemetry"),
    ];

    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["JsonConvert"] = "Newtonsoft.Json",
        ["JObject"] = "Newtonsoft.Json",
        ["JToken"] = "Newtonsoft.Json",
        ["JArray"] = "Newtonsoft.Json",
        ["JsonProperty"] = "Newtonsoft.Json",
        ["ILogger"] = "Microsoft.Extensions.Logging.Abstractions",
        ["ILoggerFactory"] = "Microsoft.Extensions.Logging.Abstractions",
        ["IServiceCollection"] = "Microsoft.Extensions.DependencyInjection.Abstractions",
        ["ServiceCollection"] = "Microsoft.Extensions.DependencyInjection",
        ["IServiceProvider"] = "Microsoft.Extensions.DependencyInjection",
        ["IConfiguration"] = "Microsoft.Extensions.Configuration.Abstractions",
        ["ConfigurationBuilder"] = "Microsoft.Extensions.Configuration",
        ["IOptions"] = "Microsoft.Extensions.Options",
        ["IHost"] = "Microsoft.Extensions.Hosting",
        ["IHostBuilder"] = "Microsoft.Extensions.Hosting",
        ["IHttpClientFactory"] = "Microsoft.Extensions.Http",
        ["IMemoryCache"] = "Microsoft.Extensions.Caching.Memory",
        ["DbContext"] = "Microsoft.EntityFrameworkCore",
        ["DbSet"] = "Microsoft.EntityFrameworkCore",
        ["ModelBuilder"] = "Microsoft.EntityFrameworkCore",
        ["SqlConnection"] = "Microsoft.Data.SqlClient",
        ["SqlCommand"] = "Microsoft.Data.SqlClient",
        ["SqliteConnection"] = "Microsoft.Data.Sqlite",
        ["WebApplicationFactory"] = "Microsoft.AspNetCore.Mvc.Testing",
        ["IMapper"] = "AutoMapper",
        ["IMediator"] = "MediatR",
        ["IRequestHandler"] = "MediatR",
        ["AbstractValidator"] = "FluentValidation",
        ["Mock"] = "Moq",
        ["Substitute"] = "NSubstitute",
        ["Fact"] = "xunit",
        ["FactAttribute"] = "xunit",
        ["Theory"] = "xunit",
        ["InlineData"] = "xunit",
        ["TestFixture"] = "NUnit",
        ["TestCase"] = "NUnit",
        ["TestClass"] = "MSTest.TestFramework",
        ["TestMethod"] = "MSTest.TestFramework",
        ["Faker"] = "Bogus",
        ["CsvReader"] = "CsvHelper",
        ["RestClient"] = "RestSharp",
        ["ConnectionMultiplexer"] = "StackExchange.Redis",
        ["MongoClient"] = "MongoDB.Driver",
        ["NpgsqlConnection"] = "Npgsql",
        ["AnsiConsole"] = "Spectre.Console",
        ["HtmlDocument"] = "HtmlAgilityPack",
        ["JwtSecurityTokenHandler"] = "System.IdentityModel.Tokens.Jwt",
        ["JwtSecurityToken"] = "System.IdentityModel.Tokens.Jwt",
        ["GrpcChannel"] = "Grpc.Net.Client",
        ["BlobServiceClient"] = "Azure.Storage.Blobs",
        ["DefaultAzureCredential"] = "Azure.Identity",
    };

    /// <summary>Extension methods that only appear once a package (or a using) is present.</summary>
    private static readonly Dictionary<string, (string Package, string Namespace)> ExtensionMethods = new(StringComparer.Ordinal)
    {
        ["AddDbContext"] = ("Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore"),
        ["UseSqlServer"] = ("Microsoft.EntityFrameworkCore.SqlServer", "Microsoft.EntityFrameworkCore"),
        ["UseNpgsql"] = ("Npgsql.EntityFrameworkCore.PostgreSQL", "Microsoft.EntityFrameworkCore"),
        ["UseSqlite"] = ("Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore"),
        ["UseInMemoryDatabase"] = ("Microsoft.EntityFrameworkCore.InMemory", "Microsoft.EntityFrameworkCore"),
        ["AddHttpClient"] = ("Microsoft.Extensions.Http", "Microsoft.Extensions.DependencyInjection"),
        ["AddMemoryCache"] = ("Microsoft.Extensions.Caching.Memory", "Microsoft.Extensions.DependencyInjection"),
        ["AddSwaggerGen"] = ("Swashbuckle.AspNetCore", "Microsoft.Extensions.DependencyInjection"),
        ["UseSwaggerUI"] = ("Swashbuckle.AspNetCore", "Microsoft.AspNetCore.Builder"),
        ["AddAutoMapper"] = ("AutoMapper", "Microsoft.Extensions.DependencyInjection"),
        ["AddMediatR"] = ("MediatR", "Microsoft.Extensions.DependencyInjection"),
        ["AddPolicyHandler"] = ("Microsoft.Extensions.Http.Polly", "Microsoft.Extensions.DependencyInjection"),
        ["AddJsonFile"] = ("Microsoft.Extensions.Configuration.Json", "Microsoft.Extensions.Configuration"),
        ["AddEnvironmentVariables"] = ("Microsoft.Extensions.Configuration.EnvironmentVariables", "Microsoft.Extensions.Configuration"),
        ["AddUserSecrets"] = ("Microsoft.Extensions.Configuration.UserSecrets", "Microsoft.Extensions.Configuration"),
        ["Bind"] = ("Microsoft.Extensions.Configuration.Binder", "Microsoft.Extensions.Configuration"),
        ["GetValue"] = ("Microsoft.Extensions.Configuration.Binder", "Microsoft.Extensions.Configuration"),
        ["AddConsole"] = ("Microsoft.Extensions.Logging.Console", "Microsoft.Extensions.Logging"),
        ["Should"] = ("FluentAssertions", "FluentAssertions"),
        ["ShouldBe"] = ("Shouldly", "Shouldly"),
        ["QueryAsync"] = ("Dapper", "Dapper"),
        ["ExecuteAsync"] = ("Dapper", "Dapper"),
    };

    private static readonly HashSet<string> LinqMethods = new(StringComparer.Ordinal)
    {
        "Where", "Select", "SelectMany", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Last", "LastOrDefault",
        "Any", "All", "Count", "Sum", "Min", "Max", "Average", "OrderBy", "OrderByDescending", "ThenBy", "GroupBy",
        "ToList", "ToArray", "ToDictionary", "ToHashSet", "Distinct", "Skip", "Take", "Concat", "Zip", "Aggregate", "Contains",
    };

    /// <summary>Package for a type or namespace name such as <c>JsonConvert</c> or <c>Newtonsoft.Json</c>.</summary>
    public static string? FindPackageForName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var trimmed = name.Trim();
        var genericTick = trimmed.IndexOfAny(['<', '`']);
        if (genericTick > 0)
        {
            trimmed = trimmed[..genericTick];
        }

        if (Types.TryGetValue(trimmed, out var package))
        {
            return package;
        }

        // Longest namespace that equals the name, starts with it, or that the name is the root of.
        foreach (var (ns, pkg) in Namespaces.OrderByDescending(n => n.Namespace.Length))
        {
            if (trimmed.Equals(ns, StringComparison.Ordinal) ||
                trimmed.StartsWith(ns + ".", StringComparison.Ordinal))
            {
                return pkg;
            }
        }

        foreach (var (ns, pkg) in Namespaces)
        {
            var root = ns.Split('.')[0];
            if (trimmed.Equals(root, StringComparison.Ordinal) && root is not ("Microsoft" or "System" or "Azure" or "Google"))
            {
                return pkg;
            }
        }

        return null;
    }

    /// <summary>For CS1061: the package and namespace that usually provide an extension method.</summary>
    public static (string? Package, string Namespace)? FindExtensionMethod(string member)
    {
        if (LinqMethods.Contains(member))
        {
            return (null, "System.Linq");
        }

        return ExtensionMethods.TryGetValue(member, out var value) ? value : null;
    }
}
