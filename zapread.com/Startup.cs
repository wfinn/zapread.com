﻿using Hangfire;
using Microsoft.Owin;
using Owin;
using System.Web;

[assembly: OwinStartupAttribute(typeof(zapread.com.Startup))]
namespace zapread.com
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // Set DB used by Hangfire
            GlobalConfiguration.Configuration.UseSqlServerStorage("ZapreadAzure");

            ConfigureAuth(app);

            app.MapSignalR();

            var options = new DashboardOptions
            {
                AppPath = VirtualPathUtility.ToAbsolute("~"),
                Authorization = new[] { new ZapReadHangFireAuthFilter() }
            };
            app.UseHangfireDashboard("/hangfire", options);
            app.UseHangfireServer();
        }
    }
}
