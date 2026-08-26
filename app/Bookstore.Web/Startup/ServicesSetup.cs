using Amazon.Rekognition;
using Amazon.S3;
using Amazon.SecretsManager.Model;
using Amazon.SecretsManager;
using Bookstore.Data;
using Bookstore.Domain.AdminUser;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Npgsql;

namespace Bookstore.Web.Startup
{
    public static class ServicesSetup
    {
        public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
        {
            builder.Services.AddControllersWithViews(x =>
            {
                x.Filters.Add(new AuthorizeFilter());
                x.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            });

            builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
            builder.Services.AddAWSService<IAmazonS3>();
            builder.Services.AddAWSService<IAmazonRekognition>();

            var connString = GetDatabaseConnectionString(builder.Configuration);
            builder.Services.AddDbContext<ApplicationDbContext>(option => option.UseNpgsql(connString));
            builder.Services.AddSession();

            return builder;
        }

        // If we find a non-empty connection string in appsettings, use it, otherwise
        // attempt to build it from data in Secrets Manager
        private static string GetDatabaseConnectionString(ConfigurationManager configuration)
        {
            // This is the key of a string value in Parameter Store containing the name of the
            // secret in Secrets Manager that in turn contains the credentials of the database in
            // Amazon RDS. The reason for the indirection is that a secret name is suffixed automatically
            // by the CDK with a random string. Using a fixed Parameter Store value to point to the
            // randomly-named secret insulates the application from variability in the name of
            // the secret.
            const string DbSecretsParameterName = "dbsecretsname";

            var connString = configuration.GetConnectionString("BookstoreDbDefaultConnection");
            if (!string.IsNullOrEmpty(connString))
            {
                Console.WriteLine("Using localdb connection string");
                return connString;
            }

            try
            {
                var dbSecretId = configuration[DbSecretsParameterName];
                Console.WriteLine($"Reading db credentials from secret {dbSecretId}");

                IAmazonSecretsManager secretsManagerClient;
                var options = configuration.GetAWSOptions();
                if (options != null)
                {
                    secretsManagerClient = options.CreateServiceClient<IAmazonSecretsManager>();
                }
                else
                {
                    secretsManagerClient = new AmazonSecretsManagerClient();
                }
                var response = secretsManagerClient.GetSecretValueAsync(new GetSecretValueRequest
                {
                    SecretId = dbSecretId
                }).Result;

                var dbSecrets = JsonSerializer.Deserialize<DbSecrets>(response.SecretString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = dbSecrets.Host,
                    Port = dbSecrets.Port,
                    Database = "BobsUsedBookStore",
                    Username = dbSecrets.Username,
                    Password = dbSecrets.Password,
                    SearchPath = "bobsusedbookstore_dbo"
                };

                connString = builder.ConnectionString;
            }
            catch (AmazonSecretsManagerException e)
            {
                Console.WriteLine($"Failed to read secret {configuration[DbSecretsParameterName]}, error {e.Message}, inner {e.InnerException.Message}");
            }
            catch (JsonException e)
            {
                Console.WriteLine($"Failed to parse content for secret {configuration[DbSecretsParameterName]}, error {e.Message}");
            }

            return connString;
        }
    }
}
