using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pheme.Diagnostics;
using Pheme.Pipeline;
using Pheme.Sending;

namespace Pheme.Registration
{
    internal static class CoreServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the services every Pheme registration needs: a scoped <see cref="ISender"/>, the options it reads
        /// the handler validation from, the pipeline behaviors and the message instrumentation.
        /// </summary>
        public static IServiceCollection AddPhemeCore(this IServiceCollection services)
        {
            services.AddOptions();
            PipelineBehaviors.GetOrAdd(services);
            services.TryAddSingleton<MessageInstrumentation>();
            services.TryAddScoped<ISender, Sender>();
            return services;
        }
    }
}
