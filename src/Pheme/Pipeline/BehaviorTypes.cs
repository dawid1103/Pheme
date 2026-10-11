using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Pheme.Internal;

namespace Pheme.Pipeline
{
    /// <summary>
    /// Checks pipeline behavior types and closes open generic ones for a message, by reflection.
    /// </summary>
    /// <remarks>
    /// Generic constraints are checked before closing, so a behavior that does not apply to a message is skipped
    /// without the exception <see cref="Type.MakeGenericType"/> would throw.
    /// </remarks>
    internal static class BehaviorTypes
    {
        /// <summary>
        /// Throws when <paramref name="behaviorType"/> cannot be used as a pipeline behavior.
        /// </summary>
        public static void Validate(Type behaviorType, string paramName)
        {
            string name = TypeNames.Format(behaviorType);
            if (!behaviorType.IsClass || behaviorType.IsAbstract)
            {
                throw new ArgumentException($"Behavior '{name}' is not a non-abstract class.", paramName);
            }

            if (behaviorType.ContainsGenericParameters && !behaviorType.IsGenericTypeDefinition)
            {
                throw new ArgumentException(
                    $"Behavior '{name}' is partially closed. Pass a closed type or an open generic type such as typeof(MyBehavior<,>).",
                    paramName);
            }

            Type[] behaviorInterfaces = GetBehaviorInterfaces(behaviorType);
            if (behaviorInterfaces.Length == 0)
            {
                throw new ArgumentException($"Behavior '{name}' does not implement IPipelineBehavior<TMessage, TResponse>.", paramName);
            }

            if (behaviorType.IsGenericTypeDefinition && !behaviorInterfaces.Any(behaviorInterface => UsesAllTypeParameters(behaviorType, behaviorInterface)))
            {
                throw new ArgumentException(
                    $"Behavior '{name}' has type parameters that Pheme cannot infer from the message and response types. "
                    + "Every type parameter of an open generic behavior must appear in its IPipelineBehavior<TMessage, TResponse> interface.",
                    paramName);
            }
        }

        /// <summary>
        /// Gets the type of <paramref name="behaviorType"/> that implements <paramref name="serviceType"/>, a closed
        /// <see cref="IPipelineBehavior{TMessage, TResponse}"/>: the type itself when it is closed, the open generic type
        /// closed with inferred type arguments otherwise. Returns <see langword="false"/> when the behavior does not
        /// apply to that message.
        /// </summary>
        public static bool TryClose(Type behaviorType, Type serviceType, [NotNullWhen(true)] out Type? closedType)
        {
            if (!behaviorType.IsGenericTypeDefinition)
            {
                closedType = serviceType.IsAssignableFrom(behaviorType) ? behaviorType : null;
                return closedType is not null;
            }

            Type[] serviceArguments = serviceType.GetGenericArguments();
            int parameterCount = behaviorType.GetGenericArguments().Length;
            foreach (Type behaviorInterface in GetBehaviorInterfaces(behaviorType))
            {
                var arguments = new Type[parameterCount];
                if (Bind(behaviorInterface.GetGenericArguments(), serviceArguments, arguments)
                    && arguments.All(argument => argument is not null)
                    && SatisfiesConstraints(behaviorType, arguments))
                {
                    Type candidate = behaviorType.MakeGenericType(arguments);
                    if (serviceType.IsAssignableFrom(candidate))
                    {
                        closedType = candidate;
                        return true;
                    }
                }
            }

            closedType = null;
            return false;
        }

        private static Type[] GetBehaviorInterfaces(Type behaviorType)
        {
            return [.. behaviorType.GetInterfaces().Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>))];
        }

        private static bool UsesAllTypeParameters(Type definition, Type behaviorInterface)
        {
            var used = new HashSet<int>();
            CollectTypeParameters(behaviorInterface, used);
            return used.Count == definition.GetGenericArguments().Length;
        }

        private static void CollectTypeParameters(Type type, HashSet<int> positions)
        {
            if (type.IsGenericParameter)
            {
                positions.Add(type.GenericParameterPosition);
            }
            else if (type.HasElementType)
            {
                CollectTypeParameters(type.GetElementType()!, positions);
            }
            else if (type.IsGenericType)
            {
                foreach (Type argument in type.GetGenericArguments())
                {
                    CollectTypeParameters(argument, positions);
                }
            }
        }

        private static bool Bind(Type[] patterns, Type[] actuals, Type[] arguments)
        {
            for (int index = 0; index < patterns.Length; index++)
            {
                if (!Bind(patterns[index], actuals[index], arguments))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Matches <paramref name="pattern"/>, a type from the behavior's interface that may contain its type
        /// parameters, against <paramref name="actual"/>, and records the type argument of each type parameter.
        /// </summary>
        private static bool Bind(Type pattern, Type actual, Type[] arguments)
        {
            if (pattern.IsGenericParameter)
            {
                ref Type bound = ref arguments[pattern.GenericParameterPosition];
                bound ??= actual;
                return bound == actual;
            }

            if (!pattern.ContainsGenericParameters)
            {
                // Compared once the behavior is closed, where IsAssignableFrom honours the variance of IPipelineBehavior.
                return true;
            }

            if (pattern.IsArray)
            {
                return actual.IsArray
                    && pattern.IsSZArray == actual.IsSZArray
                    && pattern.GetArrayRank() == actual.GetArrayRank()
                    && Bind(pattern.GetElementType()!, actual.GetElementType()!, arguments);
            }

            return pattern.IsGenericType
                && actual.IsGenericType
                && pattern.GetGenericTypeDefinition() == actual.GetGenericTypeDefinition()
                && Bind(pattern.GetGenericArguments(), actual.GetGenericArguments(), arguments);
        }

        private static bool SatisfiesConstraints(Type definition, Type[] arguments)
        {
            Type[] parameters = definition.GetGenericArguments();
            for (int index = 0; index < parameters.Length; index++)
            {
                Type argument = arguments[index];
                GenericParameterAttributes special = parameters[index].GenericParameterAttributes;

                if (special.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint) && argument.IsValueType)
                {
                    return false;
                }

                if (special.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint)
                    && (!argument.IsValueType || Nullable.GetUnderlyingType(argument) is not null))
                {
                    return false;
                }

                if (special.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint)
                    && !argument.IsValueType
                    && (argument.IsAbstract || argument.GetConstructor(Type.EmptyTypes) is null))
                {
                    return false;
                }

                foreach (Type constraint in parameters[index].GetGenericParameterConstraints())
                {
                    if (Substitute(constraint, arguments) is not { } closedConstraint || !closedConstraint.IsAssignableFrom(argument))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Replaces the type parameters in <paramref name="type"/>, a generic constraint, with their type arguments.
        /// Returns <see langword="null"/> when the constraint cannot be constructed for them, which the arguments
        /// then cannot satisfy either.
        /// </summary>
        private static Type? Substitute(Type type, Type[] arguments)
        {
            if (type.IsGenericParameter)
            {
                return arguments[type.GenericParameterPosition];
            }

            if (!type.ContainsGenericParameters)
            {
                return type;
            }

            if (type.IsArray)
            {
                Type? element = Substitute(type.GetElementType()!, arguments);
                return element is null ? null : type.IsSZArray ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
            }

            if (!type.IsGenericType)
            {
                return null;
            }

            var typeArguments = new Type[type.GetGenericArguments().Length];
            for (int index = 0; index < typeArguments.Length; index++)
            {
                if (Substitute(type.GetGenericArguments()[index], arguments) is not { } typeArgument)
                {
                    return null;
                }

                typeArguments[index] = typeArgument;
            }

            try
            {
                return type.GetGenericTypeDefinition().MakeGenericType(typeArguments);
            }
            catch (ArgumentException)
            {
                // The constraint's own constraints are violated. Checking them here instead would recurse forever on
                // self-referencing constraints such as 'where T : Entity<T>'; this path is rare, so the exception is fine.
                return null;
            }
        }
    }
}
