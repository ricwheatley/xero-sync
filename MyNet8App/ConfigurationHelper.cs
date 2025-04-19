using Microsoft.Extensions.Configuration;
using System;
using System.IO;

public static class ConfigurationHelper
{
    private static readonly IConfigurationRoot _config;

    static ConfigurationHelper()
    {
        // Set the base path so AddJsonFile looks in the output folder
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        _config = builder.Build();
    }

    public static string Get(string key)
    {
        var value = _config[key];
        if (string.IsNullOrEmpty(value))
            throw new Exception($"Configuration key '{key}' not found or empty in appsettings.json");
        return value;
    }
}
