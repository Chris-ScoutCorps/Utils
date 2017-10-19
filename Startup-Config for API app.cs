using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyCompany.Common.Extensions;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace MyCompany.Web.Public
{
    public abstract class MyCompanyBaseConfig
    {
        public static void Bind<T>(T myConfig, IConfiguration sysConfig) where T : MyCompanyBaseConfig
        {
            sysConfig.Bind(myConfig);

            foreach (var p in myConfig.GetType().GetProperties())
            {
                //Azure app settings convention
                var v = Environment.GetEnvironmentVariable("SQLCONNSTR_" + p.Name) ?? Environment.GetEnvironmentVariable("APPSETTING_" + p.Name);
                if (v != null)
                    p.SetValue(myConfig, v);
            }
        }

        public string URL { get; set; }
        public string DbConnectionString { get; set; }
    }

    public class MyCompanyConfig : MyCompanyBaseConfig
    {
        public static void Configure(IConfiguration config)
        {
            var cfg = new MyCompanyConfig();
            MyCompanyBaseConfig.Bind(cfg, config);
            Current = cfg;

            MyCompany.Data.Public.Configuration.Configure(cfg.DbConnectionString);
        }
        
        public static MyCompanyConfig Current { get; private set; }
    }

    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddDistributedMemoryCache();
            services.AddSession(options =>
            {
                options.Cookie.Name = "MyCompany";
#if DEBUG
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
#else
                options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
#endif
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
                options.IdleTimeout = TimeSpan.FromSeconds(60 * 30); 
            });

            services.AddMvc();
            MyCompanyConfig.Configure(Configuration.GetSection("MyCompanyEnvironment"));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseSession();
            app.Use(async (context, next) =>
            {
                context.Session.Set("exists", new byte[] { 1 });
                await next.Invoke();
            });

            if (env.EnvironmentName.InCaseInsensitive("development", "development-unsafe", "test"))
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Home/Error");
            }

            app.UseStatusCodePagesWithRedirects("/Home/Error/{0}");
            app.UseStaticFiles();

            app.UseMvc(routes =>
            {
                routes.MapRoute(
                    name: "default",
                    template: "{controller=Home}/{action=Index}/{id?}");
            });
        }
    }

    namespace Controllers
    {
        public class HomeController : Controller
        {
            public IActionResult Index()
            {
                return File("/index.html", "text/html");
            }

            [HttpGet("Home/Error/{code}")]
            public IActionResult Error([FromRoute] int code)
            {
                ViewBag.code = code;
                return View();
            }

            [HttpGet()]
            public IActionResult Error()
            {
                ViewBag.code = 500;
                return View();
            }
        }
    }
}
