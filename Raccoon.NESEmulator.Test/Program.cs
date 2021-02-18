using Microsoft.Extensions.DependencyInjection;
using Raccoon.Devkits.InterceptProxy;
using System;
using System.Linq;

namespace Raccoon.NESEmulator.Test
{
    class Program
    {
        static IServiceProvider serviceProvider;
        static void Build()
        {
            IServiceCollection services = new ServiceCollection();
            services.AddScoped<IMapper,Ram64K>();
            services.AddScopedProxy<ICPU, CPU>();
            services.AddScoped<TestSuite>();
            services.AddTransient<CPUExecuteLogInterceptor>();
            serviceProvider = services.BuildServiceProvider();
        }
        static void Main(string[] args)
        {
            Build();
            using IServiceScope scope = serviceProvider.CreateScope();
            TestSuite testSuite = scope.ServiceProvider.GetRequiredService<TestSuite>();
            bool test1 = testSuite.TestQuick();
            //bool test2 = testSuite.TestFull();
            
        }
    }
}
