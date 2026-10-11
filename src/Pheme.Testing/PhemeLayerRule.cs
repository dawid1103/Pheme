using System.Reflection;
using System.Runtime.CompilerServices;

namespace Pheme
{
    /// <summary>
    /// What one layer of an application may use of Pheme, checked by an architecture test: the Pheme assemblies the layer
    /// may use and the types of them it may not. The rules follow the chapter "Layers" of the documentation of Pheme.
    /// </summary>
    /// <remarks>
    /// A layer is either an assembly (<see cref="Check(Assembly)"/>) or a namespace of a module's assembly
    /// (<see cref="Check(Assembly, string)"/>). Every Pheme assembly a rule does not allow is forbidden, so a Pheme
    /// package added later is forbidden too until a rule allows it. The check needs no test framework: it throws one
    /// exception with every violation found.
    /// </remarks>
    public sealed class PhemeLayerRule
    {
        private const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // In Pheme.Abstractions for the stores of other storage, not for the application: a layer using them would read
        // or write the outbox behind the writer and the processor.
        private static readonly Type[] StoreContracts =
            [typeof(IOutboxStore), typeof(IOutboxMessageFactory), typeof(OutboxMessage), typeof(OutboxStats)];

        private readonly string name;
        private readonly string[] allowedAssemblies;
        private readonly Type[] forbiddenTypes;

        private PhemeLayerRule(string name, string[] allowedAssemblies, Type[] forbiddenTypes)
        {
            this.name = name;
            this.allowedAssemblies = allowedAssemblies;
            this.forbiddenTypes = forbiddenTypes;
        }

        /// <summary>
        /// Entities and domain events: nothing from Pheme. Domain events and the entity contract are the application's
        /// own types.
        /// </summary>
        public static PhemeLayerRule Domain { get; } = new("Domain", [], []);

        /// <summary>
        /// The messages modules share, such as integration events and the commands one module sends another:
        /// <c>Pheme.Abstractions</c> without its store contracts (<see cref="IOutboxStore"/>,
        /// <see cref="IOutboxMessageFactory"/>, <see cref="OutboxMessage"/>, <see cref="OutboxStats"/>).
        /// </summary>
        public static PhemeLayerRule Contracts { get; } = new("Contracts", ["Pheme.Abstractions"], StoreContracts);

        /// <summary>
        /// Messages, their handlers, domain and integration event handlers and <see cref="IOutboxWriter{TModule}"/>:
        /// <c>Pheme.Abstractions</c> without its store contracts (<see cref="IOutboxStore"/>,
        /// <see cref="IOutboxMessageFactory"/>, <see cref="OutboxMessage"/>, <see cref="OutboxStats"/>).
        /// </summary>
        public static PhemeLayerRule Application { get; } = new("Application", ["Pheme.Abstractions"], StoreContracts);

        /// <summary>
        /// The <c>DbContext</c> and the registration of the module, its handlers included (<c>AddPheme</c>):
        /// <c>Pheme.Abstractions</c>, <c>Pheme</c> and <c>Pheme.EntityFrameworkCore</c>.
        /// </summary>
        public static PhemeLayerRule Infrastructure { get; } =
            new("Infrastructure", ["Pheme.Abstractions", "Pheme", "Pheme.EntityFrameworkCore"], []);

        /// <summary>
        /// Checks a layer that is an assembly: the Pheme assemblies it references, and the signatures of its types for
        /// the types the layer may not use.
        /// </summary>
        /// <param name="assembly">The assembly of the layer.</param>
        /// <remarks>
        /// The compiler keeps a reference only to an assembly whose types the code uses, method bodies included, so the
        /// references show what the code uses, not what the project references. A forbidden type of an allowed assembly
        /// is found in the signatures of the types, as by <see cref="Check(Assembly, string)"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="assembly"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// The layer breaks the rule; the message lists every forbidden reference and use.
        /// </exception>
        public void Check(Assembly assembly)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            IEnumerable<string> references = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name!)
                .Where(IsForbidden)
                .Order(StringComparer.Ordinal)
                .Select(reference => $"references {reference}");
            IEnumerable<string> uses = Uses(assembly.GetTypes())
                .Where(use => forbiddenTypes.Contains(use.Type))
                .Select(use => use.Description);
            ThrowIfAny($"assembly '{assembly.GetName().Name}'", [.. references, .. Sorted(uses)]);
        }

        /// <summary>
        /// Checks a layer that is a namespace of an assembly, with the namespaces inside it: the signatures of its types
        /// for types of the Pheme assemblies the layer may not use and for the types it may not use.
        /// </summary>
        /// <param name="assembly">The assembly of the module.</param>
        /// <param name="namespace">
        /// The namespace of the layer, such as <c>Shipping.Domain</c>; its types and the types of the namespaces inside
        /// it, such as <c>Shipping.Domain.Shipments</c>, are checked, but not those of <c>Shipping.DomainEvents</c>.
        /// </param>
        /// <remarks>
        /// The signature of a type is its base types, interfaces, fields, properties, events, constructor and method
        /// parameters and return types, with their generic arguments and the element types of arrays and by-ref
        /// parameters. Method bodies, attributes and generic constraints are not checked; but the types the compiler
        /// generates from a body, such as the state machine of an async method with its local variables, are types of
        /// the namespace, checked like the others.
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="assembly"/> or <paramref name="namespace"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="namespace"/> is empty or white space, or the assembly has no types in it, so nothing would be
        /// checked; a misspelled or renamed namespace would otherwise pass.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The layer breaks the rule; the message lists every forbidden use, with the type and member.
        /// </exception>
        public void Check(Assembly assembly, string @namespace)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);

            Type[] types = [.. assembly.GetTypes().Where(type =>
                type.Namespace == @namespace || type.Namespace?.StartsWith(@namespace + ".", StringComparison.Ordinal) == true)];
            if (types.Length == 0)
            {
                throw new ArgumentException(
                    $"Assembly '{assembly.GetName().Name}' has no types in namespace '{@namespace}', so the {name} rule "
                    + "would check nothing. Is the namespace misspelled or renamed?",
                    nameof(@namespace));
            }

            IEnumerable<string> uses = Uses(types)
                .Where(use => IsForbidden(use.Type.Assembly.GetName().Name!) || forbiddenTypes.Contains(use.Type))
                .Select(use => use.Description);
            ThrowIfAny($"namespace '{@namespace}' of assembly '{assembly.GetName().Name}'", Sorted(uses));
        }

        /// <summary>
        /// Returns the name of the layer, such as <c>Domain</c>.
        /// </summary>
        /// <returns>The name of the layer.</returns>
        public override string ToString() => name;

        private bool IsForbidden(string assembly) =>
            (assembly == "Pheme" || assembly.StartsWith("Pheme.", StringComparison.Ordinal)) && !allowedAssemblies.Contains(assembly);

        private void ThrowIfAny(string layer, string[] violations)
        {
            if (violations.Length == 0)
            {
                return;
            }

            string allowed = allowedAssemblies switch
            {
                [] => "it may use no Pheme assembly",
                [string only] => $"it may use {only}",
                [.. string[] others, string last] => $"it may use {string.Join(", ", others)} and {last}",
            };
            if (forbiddenTypes.Length > 0)
            {
                allowed += $", but not {string.Join(", ", forbiddenTypes.Select(type => type.Name))}";
            }

            throw new InvalidOperationException(
                $"The {name} layer, {layer}, breaks its rule in {violations.Length} place(s); {allowed}:"
                + string.Concat(violations.Select(violation => $"{Environment.NewLine}- {violation}")));
        }

        private static string[] Sorted(IEnumerable<string> violations) => [.. violations.Distinct().Order(StringComparer.Ordinal)];

        private static IEnumerable<Use> Uses(IEnumerable<Type> types) =>
            from type in types
            from member in Signature(type)
            from used in Constituents(member.Type)
            select new Use(used, $"{type.FullName}: {member.Name} uses {used} ({used.Assembly.GetName().Name})");

        private static IEnumerable<(string Name, Type Type)> Signature(Type type)
        {
            for (Type? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                yield return ("base type", baseType);
            }

            foreach (Type @interface in type.GetInterfaces())
            {
                yield return ("interface", @interface);
            }

            // The compiler-generated fields back a property, an event or a primary constructor parameter, reported
            // instead.
            foreach (FieldInfo field in type.GetFields(Declared).Where(field => !field.IsDefined(typeof(CompilerGeneratedAttribute))))
            {
                yield return ($"field {field.Name}", field.FieldType);
            }

            foreach (PropertyInfo property in type.GetProperties(Declared))
            {
                yield return ($"property {property.Name}", property.PropertyType);
            }

            foreach (EventInfo @event in type.GetEvents(Declared))
            {
                yield return ($"event {@event.Name}", @event.EventHandlerType!);
            }

            // Accessors are reported as their property or event; operators, special names too, are kept.
            IEnumerable<MethodInfo> methods = type.GetMethods(Declared)
                .Where(method => !method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal));
            foreach (MethodBase method in type.GetConstructors(Declared).Concat<MethodBase>(methods))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    yield return ($"parameter {parameter.Name} of {method.Name}", parameter.ParameterType);
                }

                if (method is MethodInfo { ReturnType: Type returnType })
                {
                    yield return ($"return type of {method.Name}", returnType);
                }
            }
        }

        // The type and the types it is made of: generic arguments, and element types of arrays, pointers and by-ref
        // parameters.
        private static IEnumerable<Type> Constituents(Type type)
        {
            if (type.HasElementType)
            {
                return Constituents(type.GetElementType()!);
            }

            if (type.IsGenericParameter)
            {
                return [];
            }

            return [type, .. type.GetGenericArguments().SelectMany(Constituents)];
        }

        private sealed record Use(Type Type, string Description);
    }
}
