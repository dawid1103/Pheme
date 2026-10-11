using System.Reflection;

namespace Pheme.UnitTests
{
    public class SmokeTests
    {
        [Theory]
        [InlineData("Pheme")]
        [InlineData("Pheme.Abstractions")]
        public void Library_assembly_loads(string assemblyName)
        {
            var assembly = Assembly.Load(assemblyName);

            Assert.Equal(assemblyName, assembly.GetName().Name);
        }
    }
}
