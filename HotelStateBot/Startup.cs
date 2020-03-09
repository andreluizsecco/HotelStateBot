using HotelStateBot.Bots;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Azure;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Configuration;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

namespace HotelStateBot
{
    public class Startup
    {
        private bool _isProduction = false;
        public IConfiguration Configuration { get; }

        public Startup(IConfiguration configuration, IHostingEnvironment env)
        {
            Configuration = configuration;
            _isProduction = env.IsProduction();
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            services.AddBot<HotelBot>(options =>
            {
                var secretKey = Configuration.GetSection("botFileSecret")?.Value;
                var botFilePath = Configuration.GetSection("botFilePath")?.Value;

                var botConfig = BotConfiguration.Load(botFilePath ?? @"HotelStateBot.bot", secretKey);

                services.AddSingleton(sp => botConfig);

                var environment = _isProduction ? "production" : "development";
                var service = botConfig.Services.FirstOrDefault(s => s.Type == "endpoint" && s.Name == environment);
                if (!(service is EndpointService endpointService))
                {
                    throw new InvalidOperationException($"The .bot file does not contain an endpoint with name '{environment}'.");
                }

                options.CredentialProvider = new SimpleCredentialProvider(endpointService.AppId, endpointService.AppPassword);

                options.OnTurnError = async (context, exception) =>
                {
                    await context.SendActivityAsync("Sorry, it looks like something went wrong.");
                };
            });

            // MemoryStorage
            IStorage dataStore = new MemoryStorage();

            // BlobStorage (Uncomment the follow line to use Azure Blob Storage)
            //IStorage dataStore = new AzureBlobStorage("YOUR_CONNECTION_STRING", "YOUR_BLOB_CONTAINER_NAME");

            // CosmosDBStorage (Uncomment the following lines to use Azure Cosmos DB)
            //IStorage dataStore = new CosmosDbPartitionedStorage(new CosmosDbPartitionedStorageOptions
            //{
            //    CosmosDbEndpoint = CosmosServiceEndpoint,
            //    AuthKey = CosmosDBKey,
            //    DatabaseId = CosmosDBDatabaseId,
            //    ContainerId = CosmosDBContainerId,
            //});


            var conversationState = new ConversationState(dataStore);
            var userState = new UserState(dataStore);

            services.AddSingleton<BotAccessors>(sp =>
            {
                var accessors = new BotAccessors(conversationState, userState)
                {
                    DialogStateAccessor = conversationState.CreateProperty<DialogState>("DialogState"),
                    UserProfileAccessor = userState.CreateProperty<UserProfile>("UserProfile"),
                };

                return accessors;
            });
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
                app.UseDeveloperExceptionPage();
            else
                app.UseHsts();

            app.UseHttpsRedirection();

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseBotFramework();
        }
    }
}
