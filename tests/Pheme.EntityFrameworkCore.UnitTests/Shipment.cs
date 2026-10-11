namespace Pheme.EntityFrameworkCore.UnitTests
{
    public class Shipment(string name) : AggregateRoot
    {
        public int Id { get; set; }

        public string Name { get; set; } = name;
    }
}
