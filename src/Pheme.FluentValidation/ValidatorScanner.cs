using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pheme
{
    internal static class ValidatorScanner
    {
        private const int ReportedLoaderErrors = 3;

        /// <summary>
        /// Registers every concrete closed class implementing <see cref="IValidator{T}"/> in <paramref name="assembly"/>,
        /// including <see langword="internal"/> ones, as a transient service of each such interface, in the order of
        /// their full type names. A validator already registered under the interface, also by hand, is skipped.
        /// </summary>
        /// <exception cref="InvalidOperationException">Some types of <paramref name="assembly"/> cannot be loaded.</exception>
        public static void Scan(IServiceCollection services, Assembly assembly)
        {
            foreach (Type type in GetTypes(assembly).OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                // Open generic types cannot be closed for unknown type arguments, so only concrete closed classes count.
                if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
                {
                    continue;
                }

                foreach (Type serviceType in type.GetInterfaces())
                {
                    if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IValidator<>))
                    {
                        services.TryAddEnumerable(ServiceDescriptor.Transient(serviceType, type));
                    }
                }
            }
        }

        private static Type[] GetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                IEnumerable<string> reasons = exception.LoaderExceptions
                    .Select(loaderException => loaderException?.Message)
                    .OfType<string>()
                    .Distinct()
                    .Take(ReportedLoaderErrors);
                throw new InvalidOperationException(
                    $"Pheme cannot scan assembly '{assembly.GetName().Name}' for validators because some of its types "
                    + $"cannot be loaded: {string.Join(" ", reasons)} Is a dependency of the assembly missing or of another "
                    + "version?",
                    exception);
            }
        }
    }
}
