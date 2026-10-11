using Pheme.Internal;

namespace Pheme.UnitTests
{
    public class TypeNamesTests
    {
        [Theory]
        [InlineData(typeof(int), "System.Int32")]
        [InlineData(typeof(Dictionary<string, int?>), "System.Collections.Generic.Dictionary<System.String, System.Nullable<System.Int32>>")]
        [InlineData(typeof(List<int>[]), "System.Collections.Generic.List<System.Int32>[]")]
        [InlineData(typeof(List<int>[][]), "System.Collections.Generic.List<System.Int32>[][]")]
        [InlineData(typeof(int[,]), "System.Int32[,]")]
        [InlineData(typeof(List<int[,,]>), "System.Collections.Generic.List<System.Int32[,,]>")]
        public void Formats_types_as_written_in_csharp(Type type, string expected)
        {
            Assert.Equal(expected, TypeNames.Format(type));
        }

        [Fact]
        public void Formats_nested_types_with_a_dot_and_each_generic_argument_on_its_own_type()
        {
            Assert.Equal("Pheme.UnitTests.TypeNamesTests.PlaceOrder.Command", TypeNames.Format(typeof(PlaceOrder.Command)));
            Assert.Equal(
                "Pheme.UnitTests.TypeNamesTests.Outer<System.Int32>.Inner<System.Collections.Generic.List<Pheme.UnitTests.TypeNamesTests.PlaceOrder.Command>>",
                TypeNames.Format(typeof(Outer<int>.Inner<List<PlaceOrder.Command>>)));
            Assert.Equal("Pheme.UnitTests.TypeNamesTests.Outer<System.String>.Plain", TypeNames.Format(typeof(Outer<string>.Plain)));
            Assert.Equal("Pheme.UnitTests.TypeNamesTests.Outer<T>.Inner<TInner>", TypeNames.Format(typeof(Outer<>.Inner<>)));
        }

        [Fact]
        public void Stored_names_keep_the_plus_of_nested_types()
        {
            Assert.Equal("Pheme.UnitTests.TypeNamesTests+PlaceOrder+Command", TypeNames.FormatStored(typeof(PlaceOrder.Command)));
            Assert.Equal(
                "Pheme.UnitTests.TypeNamesTests+Outer<System.Int32>+Inner<Pheme.UnitTests.TypeNamesTests+PlaceOrder+Command>",
                TypeNames.FormatStored(typeof(Outer<int>.Inner<PlaceOrder.Command>)));
            Assert.Equal("System.Collections.Generic.List<System.Int32>", TypeNames.FormatStored(typeof(List<int>)));
        }

        [Fact]
        public void Formats_by_ref_and_pointer_types_with_their_element_type()
        {
            Assert.Equal("System.Collections.Generic.List<System.Int32>&", TypeNames.Format(typeof(List<int>).MakeByRefType()));
            Assert.Equal("System.Int32*", TypeNames.Format(typeof(int).MakePointerType()));
            Assert.Equal("System.Int32[*]", TypeNames.Format(typeof(int).MakeArrayType(1)));
        }

        private static class PlaceOrder
        {
            public sealed record Command;
        }

        private static class Outer<T>
        {
            public sealed class Inner<TInner>;

            public sealed class Plain;
        }
    }
}
