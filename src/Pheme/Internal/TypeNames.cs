using System.Text.RegularExpressions;

namespace Pheme.Internal
{
    /// <summary>
    /// Formats types for messages the way they are written in C#: <c>Ns.Handler&lt;Ns.Order, System.Guid&gt;</c>
    /// instead of the assembly-qualified generic arguments of <see cref="Type.FullName"/>, and <c>Ns.PlaceOrder.Command</c>
    /// for a nested type.
    /// </summary>
    internal static partial class TypeNames
    {
        public static string Format(Type type)
        {
            return Format(type, '.');
        }

        /// <summary>
        /// Formats <paramref name="type"/> as <see cref="Format(Type)"/> does, but a nested type with <c>+</c>, as
        /// <see cref="Type.FullName"/> has it: a name stored as data, such as a completed handler of an outbox message,
        /// where <c>Ns.Outer+Inner</c> must not equal the type <c>Inner</c> of the namespace <c>Ns.Outer</c>.
        /// </summary>
        public static string FormatStored(Type type)
        {
            return Format(type, '+');
        }

        private static string Format(Type type, char nestedSeparator)
        {
            if (type.HasElementType)
            {
                string element = Format(type.GetElementType()!, nestedSeparator);
                if (type.IsArray)
                {
                    string rank = type.IsSZArray ? string.Empty : type.GetArrayRank() == 1 ? "*" : new string(',', type.GetArrayRank() - 1);
                    return $"{element}[{rank}]";
                }

                return type.IsPointer ? $"{element}*" : $"{element}&";
            }

            Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (definition.FullName is not { } fullName)
            {
                return type.Name;
            }

            // Each type of a nesting chain takes its own generic arguments, in order: Outer`1+Inner`1 is Outer<A>.Inner<B>.
            Type[] arguments = type.GetGenericArguments();
            int next = 0;
            IEnumerable<string> segments = fullName.Split('+').Select(segment =>
            {
                Match arity = GenericArity().Match(segment);
                if (!arity.Success)
                {
                    return segment;
                }

                int count = int.Parse(arity.Groups[1].ValueSpan, provider: null);
                Type[] own = arguments[next..(next += count)];
                return $"{segment[..arity.Index]}<{string.Join(", ", own.Select(argument => Format(argument, nestedSeparator)))}>";
            });
            return string.Join(nestedSeparator, segments);
        }

        [GeneratedRegex(@"`(\d+)$")]
        private static partial Regex GenericArity();
    }
}
